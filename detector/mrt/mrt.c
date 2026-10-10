#include "mrt.h"

#include <stdlib.h>
#include <string.h>

enum
{
    MRT_BGP4MP = 16,
    MRT_BGP4MP_ET = 17,
    MRT_MICROSECONDS_SIZE = 4,
    BGP4MP_MESSAGE = 1,
    BGP4MP_MESSAGE_AS4 = 4,
    BGP4MP_MESSAGE_LOCAL = 6,
    BGP4MP_MESSAGE_AS4_LOCAL = 7,
    BGP_MARKER_SIZE = 16,
    BGP_HEADER_SIZE = 19,
    BGP_UPDATE = 2,
    ATTRIBUTE_EXTENDED_LENGTH = 0x10,
    ATTRIBUTE_AS_PATH = 2,
    ATTRIBUTE_NEXT_HOP = 3,
    ATTRIBUTE_MP_REACH_NLRI = 14,
    ATTRIBUTE_MP_UNREACH_NLRI = 15,
    ATTRIBUTE_AS4_PATH = 17,
    AFI_IPV4 = 1,
    AFI_IPV6 = 2,
    SAFI_UNICAST = 1,
    MAX_RUNS = 4
};

/* A bounds-checked view of the input. Reading past the end sets *overrun and yields zeros. */
typedef struct
{
    const uint8_t *cursor;
    const uint8_t *end;
    int *overrun;
} span;

/* The prefixes of one UPDATE section, which share family, direction and next hop. */
typedef struct
{
    span prefixes;
    uint8_t family;
    uint8_t withdrawn;
    mrt_address next_hop;
} prefix_run;

typedef struct
{
    uint16_t length;
    uint32_t asns[MRT_MAX_AS_PATH];
} as_path;

struct mrt_reader
{
    span input;
    int overrun;
    int bare; /* the input is one BGP message, not MRT records */
    uint32_t timestamp;
    mrt_address peer;
    as_path path;
    as_path as4_path;
    prefix_run runs[MAX_RUNS];
    size_t run_count;
    size_t run_index;
};

static size_t remaining(const span *source)
{
    return (size_t)(source->end - source->cursor);
}

static span take(span *source, size_t size)
{
    if (size > remaining(source))
    {
        *source->overrun = 1;
        size = remaining(source);
    }
    span taken = {source->cursor, source->cursor + size, source->overrun};
    source->cursor += size;
    return taken;
}

static uint32_t read_uint(span *source, size_t width)
{
    span bytes = take(source, width);
    uint32_t value = 0;
    while (bytes.cursor < bytes.end)
    {
        value = (value << 8) | *bytes.cursor++;
    }
    return value;
}

static size_t address_size(uint8_t family)
{
    return family == 6 ? 16 : 4;
}

static mrt_address read_address(span *source, uint8_t family)
{
    mrt_address address = {family, {0}};
    span bytes = take(source, address_size(family));
    memcpy(address.bytes, bytes.cursor, remaining(&bytes));
    return address;
}

static int is_ip(uint32_t afi)
{
    return afi == AFI_IPV4 || afi == AFI_IPV6;
}

static uint8_t family_of(uint32_t afi)
{
    return afi == AFI_IPV6 ? 6 : 4;
}

static void add_run(mrt_reader *reader, prefix_run run)
{
    if (reader->run_count == MAX_RUNS)
    {
        reader->overrun = 1;
        return;
    }
    reader->runs[reader->run_count++] = run;
}

/* Flattens every segment of an AS_PATH or AS4_PATH value into path. */
static void read_as_path(span value, size_t as_width, as_path *path)
{
    path->length = 0;
    while (remaining(&value) > 0)
    {
        read_uint(&value, 1); /* segment type */
        uint32_t count = read_uint(&value, 1);
        if (path->length + count > MRT_MAX_AS_PATH)
        {
            *value.overrun = 1;
            return;
        }
        for (uint32_t i = 0; i < count; i++)
        {
            path->asns[path->length++] = read_uint(&value, as_width);
        }
    }
}

/* RFC 6793: the AS4_PATH replaces the tail of a 2-octet AS_PATH. */
static void merge_as4_path(as_path *path, const as_path *as4_path)
{
    if (as4_path->length > path->length)
    {
        return;
    }
    uint32_t *tail = path->asns + path->length - as4_path->length;
    memcpy(tail, as4_path->asns, as4_path->length * sizeof as4_path->asns[0]);
}

static void read_mp_reach(mrt_reader *reader, span value)
{
    uint32_t afi = read_uint(&value, 2);
    uint32_t safi = read_uint(&value, 1);
    uint32_t next_hop_size = read_uint(&value, 1);
    span next_hop = take(&value, next_hop_size);
    read_uint(&value, 1); /* reserved */
    if (!is_ip(afi) || safi != SAFI_UNICAST)
    {
        return;
    }
    uint8_t next_hop_family = next_hop_size >= 16 ? 6 : 4;
    add_run(reader, (prefix_run){value, family_of(afi), 0, read_address(&next_hop, next_hop_family)});
}

static void read_mp_unreach(mrt_reader *reader, span value)
{
    uint32_t afi = read_uint(&value, 2);
    uint32_t safi = read_uint(&value, 1);
    if (is_ip(afi) && safi == SAFI_UNICAST)
    {
        add_run(reader, (prefix_run){value, family_of(afi), 1, {0}});
    }
}

/* Queues the prefix runs of one UPDATE body and decodes the attributes they share. */
static void read_update(mrt_reader *reader, span update, size_t as_width)
{
    uint32_t withdrawn_size = read_uint(&update, 2);
    span withdrawn = take(&update, withdrawn_size);
    uint32_t attributes_size = read_uint(&update, 2);
    span attributes = take(&update, attributes_size);
    mrt_address next_hop = {0};

    reader->path.length = 0;
    reader->as4_path.length = 0;
    add_run(reader, (prefix_run){withdrawn, 4, 1, {0}});
    while (remaining(&attributes) > 0)
    {
        uint32_t flags = read_uint(&attributes, 1);
        uint32_t code = read_uint(&attributes, 1);
        uint32_t size = read_uint(&attributes, flags & ATTRIBUTE_EXTENDED_LENGTH ? 2 : 1);
        span value = take(&attributes, size);
        switch (code)
        {
            case ATTRIBUTE_AS_PATH:
                read_as_path(value, as_width, &reader->path);
                break;
            case ATTRIBUTE_AS4_PATH:
                read_as_path(value, 4, &reader->as4_path);
                break;
            case ATTRIBUTE_NEXT_HOP:
                next_hop = read_address(&value, 4);
                break;
            case ATTRIBUTE_MP_REACH_NLRI:
                read_mp_reach(reader, value);
                break;
            case ATTRIBUTE_MP_UNREACH_NLRI:
                read_mp_unreach(reader, value);
                break;
        }
    }
    if (as_width == 2)
    {
        merge_as4_path(&reader->path, &reader->as4_path);
    }
    add_run(reader, (prefix_run){update, 4, 0, next_hop});
}

/* Decodes one BGP message from its marker on and, when it is an UPDATE, its body. */
static void read_bgp_message(mrt_reader *reader, span *source, size_t as_width)
{
    take(source, BGP_MARKER_SIZE);
    uint32_t message_size = read_uint(source, 2);
    uint32_t message_type = read_uint(source, 1);
    if (message_size < BGP_HEADER_SIZE)
    {
        reader->overrun = 1;
        return;
    }
    if (message_type == BGP_UPDATE)
    {
        read_update(reader, take(source, message_size - BGP_HEADER_SIZE), as_width);
    }
}

/* Decodes the BGP4MP peer header and the BGP message after it. */
static void read_message(mrt_reader *reader, span record, size_t as_width)
{
    take(&record, as_width); /* peer AS */
    take(&record, as_width + 2); /* local AS, interface index */
    uint32_t afi = read_uint(&record, 2);
    uint8_t family = family_of(afi);
    reader->peer = read_address(&record, family);
    take(&record, address_size(family)); /* local address */
    if (!is_ip(afi))
    {
        reader->overrun = 1;
        return;
    }
    read_bgp_message(reader, &record, as_width);
}

static int is_message(uint32_t type, uint32_t subtype)
{
    if (type != MRT_BGP4MP && type != MRT_BGP4MP_ET)
    {
        return 0;
    }
    return subtype == BGP4MP_MESSAGE || subtype == BGP4MP_MESSAGE_AS4
        || subtype == BGP4MP_MESSAGE_LOCAL || subtype == BGP4MP_MESSAGE_AS4_LOCAL;
}

static size_t as_width_of(uint32_t subtype)
{
    return subtype == BGP4MP_MESSAGE_AS4 || subtype == BGP4MP_MESSAGE_AS4_LOCAL ? 4 : 2;
}

/* Queues the prefix runs of the next MRT record. Returns 0 at the end of the data. */
static int load_record(mrt_reader *reader)
{
    reader->run_count = 0;
    reader->run_index = 0;
    if (remaining(&reader->input) == 0)
    {
        return 0;
    }
    if (reader->bare)
    {
        span message = take(&reader->input, remaining(&reader->input));
        read_bgp_message(reader, &message, 4);
        return 1;
    }
    reader->timestamp = read_uint(&reader->input, 4);
    uint32_t type = read_uint(&reader->input, 2);
    uint32_t subtype = read_uint(&reader->input, 2);
    uint32_t size = read_uint(&reader->input, 4);
    span record = take(&reader->input, size);
    if (is_message(type, subtype))
    {
        if (type == MRT_BGP4MP_ET)
        {
            take(&record, MRT_MICROSECONDS_SIZE);
        }
        read_message(reader, record, as_width_of(subtype));
    }
    return 1;
}

static void fill_route(const mrt_reader *reader, const prefix_run *run, mrt_route *route)
{
    route->timestamp = reader->timestamp;
    route->peer = reader->peer;
    route->withdrawn = run->withdrawn;
    route->next_hop = run->next_hop;
    route->as_path_length = run->withdrawn ? 0 : reader->path.length;
    memcpy(route->as_path, reader->path.asns, route->as_path_length * sizeof route->as_path[0]);
}

/* Pops the next queued prefix into route. Returns 0 when none is left or it is malformed. */
static int take_prefix(mrt_reader *reader, mrt_route *route)
{
    while (reader->run_index < reader->run_count)
    {
        prefix_run *run = &reader->runs[reader->run_index];
        if (remaining(&run->prefixes) == 0)
        {
            reader->run_index++;
            continue;
        }
        uint32_t bits = read_uint(&run->prefixes, 1);
        span bytes = take(&run->prefixes, (bits + 7) / 8);
        if (bits > 8 * address_size(run->family))
        {
            reader->overrun = 1;
        }
        if (reader->overrun)
        {
            return 0;
        }
        fill_route(reader, run, route);
        route->prefix = (mrt_address){run->family, {0}};
        memcpy(route->prefix.bytes, bytes.cursor, remaining(&bytes));
        if (bits % 8 != 0)
        {
            route->prefix.bytes[bits / 8] &= (uint8_t)(0xFF << (8 - bits % 8));
        }
        route->prefix_length = (uint8_t)bits;
        return 1;
    }
    return 0;
}

mrt_reader *mrt_open(const uint8_t *data, size_t size)
{
    mrt_reader *reader = calloc(1, sizeof *reader);
    if (reader != NULL)
    {
        reader->input = (span){data, data + size, &reader->overrun};
    }
    return reader;
}

mrt_reader *bgp_update_open(const uint8_t *message, size_t size)
{
    mrt_reader *reader = mrt_open(message, size);
    if (reader != NULL)
    {
        reader->bare = 1;
    }
    return reader;
}

mrt_status mrt_next(mrt_reader *reader, mrt_route *route)
{
    while (!take_prefix(reader, route))
    {
        if (!reader->overrun && !load_record(reader))
        {
            return MRT_END;
        }
        if (reader->overrun)
        {
            reader->overrun = 0;
            reader->run_count = 0;
            return MRT_MALFORMED;
        }
    }
    return MRT_ROUTE;
}

void mrt_close(mrt_reader *reader)
{
    free(reader);
}
