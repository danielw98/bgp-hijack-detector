/* Reader for MRT files (RFC 6396). Decodes the BGP UPDATEs carried by BGP4MP and
   BGP4MP_ET message records into one mrt_route per IPv4/IPv6 unicast prefix. */

#ifndef MRT_H
#define MRT_H

#include <stddef.h>
#include <stdint.h>

enum { MRT_MAX_AS_PATH = 2048 };

typedef struct
{
    uint8_t family; /* 4, 6, or 0 when absent */
    uint8_t bytes[16];
} mrt_address;

typedef struct
{
    uint32_t timestamp; /* seconds since the Unix epoch */
    mrt_address peer;
    mrt_address prefix;
    uint8_t prefix_length; /* bits */
    uint8_t withdrawn;
    mrt_address next_hop;
    uint16_t as_path_length;
    uint32_t as_path[MRT_MAX_AS_PATH]; /* AS_SET members flattened in order */
} mrt_route;

typedef enum
{
    MRT_MALFORMED = -1,
    MRT_END = 0,
    MRT_ROUTE = 1
} mrt_status;

typedef struct mrt_reader mrt_reader;

/* The reader borrows data, which must outlive it. Returns NULL when out of memory. */
mrt_reader *mrt_open(const uint8_t *data, size_t size);

/* Fills route with the next prefix. MRT_MALFORMED reports one record whose remaining
   prefixes were dropped; the reader continues with the following record. */
mrt_status mrt_next(mrt_reader *reader, mrt_route *route);

void mrt_close(mrt_reader *reader);

#endif
