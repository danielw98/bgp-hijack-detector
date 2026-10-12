/* Mutation harness for the MRT parser: parses a seed file under random byte flips and
   truncations, as MRT records and, from a BGP marker of the seed on, as one bare BGP
   message. Built with ASan and UBSan by fuzz.sh, so any out-of-bounds read or
   undefined operation aborts the run. Every mutated input lives in a heap block of its
   exact size, so a read one byte past it is caught.

   Usage: fuzz <seed.mrt> <iterations>
   Exit status: 0 on success, 1 when a step fails (the message is on stderr), 2 on bad usage. */

#include "mrt.h"

#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

enum
{
    MAX_FLIPS = 32,
    MARKER_SIZE = 16,
    EXIT_OK = 0,
    EXIT_FAILED = 1,
    EXIT_USAGE = 2
};

typedef struct
{
    unsigned long long routes;
    unsigned long long malformed;
} tally;

static uint64_t state = 0x9E3779B97F4A7C15u;

static uint64_t next_random(void)
{
    state ^= state << 13;
    state ^= state >> 7;
    state ^= state << 17;
    return state;
}

/* A block of at least one byte, so a zero size never yields a NULL that means success. */
static void *allocate(size_t size)
{
    return malloc(size == 0 ? 1 : size);
}

/* Parses a decimal count with no sign, no trailing text and no overflow. Returns 0 on success. */
static int parse_count(const char *text, size_t *count)
{
    if (text == NULL || *text < '0' || *text > '9')
    {
        return -1;
    }
    char *end = NULL;
    errno = 0;
    unsigned long long value = strtoull(text, &end, 10);
    if (errno != 0 || end == text || *end != '\0' || value > SIZE_MAX)
    {
        return -1;
    }
    *count = (size_t)value;
    return 0;
}

/* Reads a whole file into a new heap block. Returns 0 on success; on failure prints why
   and leaves *data NULL. */
static int read_file(const char *path, uint8_t **data, size_t *size)
{
    *data = NULL;
    *size = 0;
    FILE *file = fopen(path, "rb");
    if (file == NULL)
    {
        perror(path);
        return -1;
    }
    int result = -1;
    long length = -1;
    if (fseek(file, 0, SEEK_END) != 0 || (length = ftell(file)) < 0 || fseek(file, 0, SEEK_SET) != 0)
    {
        perror(path);
    }
    else if ((unsigned long)length > SIZE_MAX)
    {
        fprintf(stderr, "%s: too large to read\n", path);
    }
    else if ((*data = allocate((size_t)length)) == NULL)
    {
        fprintf(stderr, "%s: out of memory\n", path);
    }
    else if (fread(*data, 1, (size_t)length, file) != (size_t)length)
    {
        fprintf(stderr, "%s: %s\n", path, ferror(file) ? "read error" : "file shrank while read");
    }
    else
    {
        *size = (size_t)length;
        result = 0;
    }
    if (fclose(file) != 0 && result == 0)
    {
        perror(path);
        result = -1;
    }
    if (result != 0)
    {
        free(*data);
        *data = NULL;
        *size = 0;
    }
    return result;
}

/* An exact-size heap copy of the seed, cut short on odd iterations, with flipped bytes.
   Returns NULL when out of memory. */
static uint8_t *mutated(const uint8_t *seed, size_t seed_size, size_t iteration, size_t *size)
{
    *size = iteration % 2 == 1 ? (size_t)(next_random() % ((uint64_t)seed_size + 1)) : seed_size;
    uint8_t *data = allocate(*size);
    if (data == NULL)
    {
        return NULL;
    }
    if (*size > 0)
    {
        memcpy(data, seed, *size);
    }
    size_t flips = *size == 0 ? 0 : 1 + (size_t)(next_random() % MAX_FLIPS);
    for (size_t i = 0; i < flips; i++)
    {
        data[next_random() % *size] = (uint8_t)next_random();
    }
    return data;
}

/* The offset of a BGP marker (sixteen 0xFF octets) in data, picked at random; size when none. */
static size_t marker_offset(const uint8_t *data, size_t size)
{
    size_t count = 0;
    size_t picked = size;
    size_t run = 0;
    for (size_t i = 0; i < size; i++)
    {
        run = data[i] == 0xFF ? run + 1 : 0;
        if (run == MARKER_SIZE && next_random() % ++count == 0)
        {
            picked = i + 1 - MARKER_SIZE;
        }
    }
    return picked;
}

/* Reads every route of reader into counts and closes it. Returns 0 on success; a NULL
   reader or an MRT_INVALID answer is a failure. */
static int drain(mrt_reader *reader, mrt_route *route, tally *counts)
{
    if (reader == NULL)
    {
        fprintf(stderr, "mrt reader: out of memory or invalid input range\n");
        return -1;
    }
    int result = 0;
    mrt_status status;
    while ((status = mrt_next(reader, route)) != MRT_END)
    {
        if (status == MRT_INVALID)
        {
            fprintf(stderr, "mrt_next: invalid argument\n");
            result = -1;
            break;
        }
        if (status == MRT_MALFORMED)
        {
            counts->malformed++;
        }
        else
        {
            counts->routes++;
        }
    }
    mrt_close(reader);
    return result;
}

int main(int argc, char **argv)
{
    size_t iterations = 0;
    if (argc != 3 || parse_count(argv[2], &iterations) != 0)
    {
        fprintf(stderr, "usage: fuzz <seed.mrt> <iterations>\n");
        return EXIT_USAGE;
    }
    int exit_status = EXIT_FAILED;
    uint8_t *seed = NULL;
    size_t seed_size = 0;
    mrt_route *route = malloc(sizeof *route);
    tally records = {0, 0};
    tally messages = {0, 0};
    if (route == NULL)
    {
        fprintf(stderr, "route: out of memory\n");
        goto cleanup;
    }
    if (read_file(argv[1], &seed, &seed_size) != 0)
    {
        goto cleanup;
    }
    for (size_t i = 0; i < iterations; i++)
    {
        size_t size = 0;
        uint8_t *data = mutated(seed, seed_size, i, &size);
        if (data == NULL)
        {
            fprintf(stderr, "iteration %zu: out of memory\n", i);
            goto cleanup;
        }
        size_t offset = marker_offset(data, size);
        int failed = drain(mrt_open(data, size), route, &records) != 0
            || drain(bgp_update_open(data + offset, size - offset), route, &messages) != 0;
        free(data);
        if (failed)
        {
            goto cleanup;
        }
    }
    if (printf("{\"iterations\": %zu, \"routes\": %llu, \"malformed_records\": %llu, "
               "\"message_routes\": %llu, \"malformed_messages\": %llu}\n",
               iterations, records.routes, records.malformed, messages.routes, messages.malformed) < 0
        || fflush(stdout) != 0)
    {
        perror("stdout");
        goto cleanup;
    }
    exit_status = EXIT_OK;

cleanup:
    free(route);
    free(seed);
    return exit_status;
}
