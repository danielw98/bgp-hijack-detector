/* Mutation harness for the MRT parser: parses a seed file under random byte flips and
   truncations. Built with ASan and UBSan by fuzz.sh, so any out-of-bounds read or
   undefined operation aborts the run.

   Usage: fuzz <seed.mrt> <iterations> */

#include "mrt.h"

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

enum { MAX_FLIPS = 32 };

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
    unsigned long long routes = 0;
    unsigned long long malformed = 0;

    for (size_t i = 0; i < iterations; i++)
    {
        size_t size = 0;
        uint8_t *data = mutated(seed, seed_size, i, &size);
        mrt_reader *reader = mrt_open(data, size);
        mrt_status status;
        while ((status = mrt_next(reader, route)) != MRT_END)
        {
            if (status == MRT_MALFORMED)
            {
                malformed++;
            }
            else
            {
                routes++;
            }
        }
        mrt_close(reader);
        free(data);
    }
    printf("{\"iterations\": %zu, \"routes\": %llu, \"malformed_records\": %llu}\n", iterations, routes,
           malformed);
    free(route);
    free(seed);
    return 0;
}
