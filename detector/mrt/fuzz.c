/* Mutation harness for the MRT parser: parses a seed file under random byte flips and
   truncations, as MRT records and, from a BGP marker of the seed on, as one bare BGP
   message. Built with ASan and UBSan by fuzz.sh, so any out-of-bounds read or
   undefined operation aborts the run.

   Usage: fuzz <seed.mrt> <iterations> */

#include "mrt.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

enum { MAX_FLIPS = 32, MARKER_SIZE = 16 };

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

static uint8_t *read_file(const char *path, size_t *size)
{
    FILE *file = fopen(path, "rb");
    if (file == NULL)
    {
        perror(path);
        exit(2);
    }
    fseek(file, 0, SEEK_END);
    *size = (size_t)ftell(file);
    rewind(file);
    uint8_t *data = malloc(*size);
    if (data == NULL || fread(data, 1, *size, file) != *size)
    {
        perror(path);
        exit(2);
    }
    fclose(file);
    return data;
}

/* An exact-size heap copy of the seed, cut short on odd iterations, with flipped bytes. */
static uint8_t *mutated(const uint8_t *seed, size_t seed_size, size_t iteration, size_t *size)
{
    *size = iteration % 2 == 1 ? next_random() % (seed_size + 1) : seed_size;
    uint8_t *data = malloc(*size + 1);
    if (data == NULL)
    {
        exit(2);
    }
    memcpy(data, seed, *size);
    size_t flips = *size == 0 ? 0 : 1 + next_random() % MAX_FLIPS;
    for (size_t i = 0; i < flips; i++)
    {
        data[next_random() % *size] = (uint8_t)next_random();
    }
    return realloc(data, *size == 0 ? 1 : *size);
}

/* The offset of a BGP marker (sixteen 0xFF octets) in data, picked at random. */
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

static void drain(mrt_reader *reader, mrt_route *route, tally *counts)
{
    mrt_status status;
    while ((status = mrt_next(reader, route)) != MRT_END)
    {
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
}

int main(int argc, char **argv)
{
    if (argc != 3)
    {
        fprintf(stderr, "usage: fuzz <seed.mrt> <iterations>\n");
        return 2;
    }
    size_t seed_size = 0;
    uint8_t *seed = read_file(argv[1], &seed_size);
    size_t iterations = strtoul(argv[2], NULL, 10);
    mrt_route *route = malloc(sizeof *route);
    tally records = {0, 0};
    tally messages = {0, 0};

    for (size_t i = 0; i < iterations; i++)
    {
        size_t size = 0;
        uint8_t *data = mutated(seed, seed_size, i, &size);
        drain(mrt_open(data, size), route, &records);
        size_t offset = marker_offset(data, size);
        drain(bgp_update_open(data + offset, size - offset), route, &messages);
        free(data);
    }
    printf("{\"iterations\": %zu, \"routes\": %llu, \"malformed_records\": %llu, "
           "\"message_routes\": %llu, \"malformed_messages\": %llu}\n",
           iterations, records.routes, records.malformed, messages.routes, messages.malformed);
    free(route);
    free(seed);
    return 0;
}
