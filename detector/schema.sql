-- Route history and alerts for the BGP hijack detector.
-- cidr/inet columns let Postgres do sub-prefix lookups with the containment operators.

CREATE TABLE IF NOT EXISTS monitored_prefixes (
    prefix           cidr PRIMARY KEY,
    expected_origins integer[] NOT NULL,
    description      text NOT NULL DEFAULT ''
);

CREATE TABLE IF NOT EXISTS route_updates (
    id        bigserial PRIMARY KEY,
    seen_at   timestamptz NOT NULL DEFAULT now(),
    peer      text NOT NULL,
    prefix    cidr NOT NULL,
    origin_as integer,
    as_path   integer[] NOT NULL DEFAULT '{}',
    next_hop  inet,
    withdrawn boolean NOT NULL DEFAULT false
);
CREATE INDEX IF NOT EXISTS route_updates_prefix_gist ON route_updates USING gist (prefix inet_ops);
CREATE INDEX IF NOT EXISTS route_updates_seen_at ON route_updates (seen_at DESC);

CREATE TABLE IF NOT EXISTS alerts (
    id               bigserial PRIMARY KEY,
    raised_at        timestamptz NOT NULL DEFAULT now(),
    alert_type       text NOT NULL,
    prefix           cidr NOT NULL,
    observed_origin  integer,
    expected_origins integer[] NOT NULL DEFAULT '{}',
    covering_prefix  cidr,
    peer             text NOT NULL DEFAULT '',
    as_path          integer[] NOT NULL DEFAULT '{}',
    detail           text NOT NULL DEFAULT ''
);
CREATE INDEX IF NOT EXISTS alerts_prefix_gist ON alerts USING gist (prefix inet_ops);
CREATE INDEX IF NOT EXISTS alerts_raised_at ON alerts (raised_at DESC);
