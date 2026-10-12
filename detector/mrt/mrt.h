/* Reader for MRT files (RFC 6396) and for bare BGP UPDATE messages. Decodes the
   UPDATEs carried by BGP4MP and BGP4MP_ET message records, or one UPDATE as BMP route
   monitoring carries it (RFC 7854), into one mrt_route per IPv4/IPv6 unicast prefix. */

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
    MRT_INVALID = -2, /* a NULL reader or route */
    MRT_MALFORMED = -1,
    MRT_END = 0,
    MRT_ROUTE = 1
} mrt_status;

typedef struct mrt_reader mrt_reader;

/* The reader borrows data, which must outlive it. data may be NULL only when size is 0.
   Returns NULL when out of memory, or when data and size do not form a valid range. */
mrt_reader *mrt_open(const uint8_t *data, size_t size);

/* A reader over one BGP message (RFC 4271, from its marker on) with four-octet AS
   numbers, as BMP carries it. Its routes have no timestamp and no peer: the caller
   knows both. Borrows message and returns NULL like mrt_open. */
mrt_reader *bgp_update_open(const uint8_t *message, size_t size);

/* Fills route with the next prefix. MRT_MALFORMED reports one record whose remaining
   prefixes were dropped; the reader continues with the following record. MRT_INVALID
   reports a NULL reader or route and touches nothing. */
mrt_status mrt_next(mrt_reader *reader, mrt_route *route);

/* Frees the reader; a NULL reader is ignored. */
void mrt_close(mrt_reader *reader);

#endif
