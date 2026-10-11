-- Route history and alerts for the BGP hijack detector.
-- cidr/inet columns let Postgres do sub-prefix lookups with the containment operators.
-- source names where a row came from: 'lab' for the live collector, the incident
-- directory name for a replay, so a replay can be loaded once and replaced as a unit.
-- AS numbers are bigint, holding every four-octet ASN up to 4294967295.

CREATE TABLE IF NOT EXISTS monitored_prefixes (
    prefix           cidr PRIMARY KEY,
    expected_origins bigint[] NOT NULL,
    description      text NOT NULL DEFAULT ''
);
ALTER TABLE monitored_prefixes ALTER COLUMN expected_origins TYPE bigint[];

CREATE TABLE IF NOT EXISTS route_updates (
    id        bigserial PRIMARY KEY,
    seen_at   timestamptz NOT NULL DEFAULT now(),
    peer      text NOT NULL,
    prefix    cidr NOT NULL,
    origin_as bigint,
    as_path   bigint[] NOT NULL DEFAULT '{}',
    next_hop  inet,
    withdrawn boolean NOT NULL DEFAULT false,
    source    text NOT NULL DEFAULT 'lab'
);
ALTER TABLE route_updates ADD COLUMN IF NOT EXISTS source text NOT NULL DEFAULT 'lab';
ALTER TABLE route_updates ALTER COLUMN origin_as TYPE bigint, ALTER COLUMN as_path TYPE bigint[];
CREATE INDEX IF NOT EXISTS route_updates_prefix_gist ON route_updates USING gist (prefix inet_ops);
CREATE INDEX IF NOT EXISTS route_updates_seen_at ON route_updates (seen_at DESC);
CREATE INDEX IF NOT EXISTS route_updates_source ON route_updates (source);

CREATE TABLE IF NOT EXISTS alerts (
    id               bigserial PRIMARY KEY,
    raised_at        timestamptz NOT NULL DEFAULT now(),
    alert_type       text NOT NULL,
    prefix           cidr NOT NULL,
    observed_origin  bigint,
    expected_origins bigint[] NOT NULL DEFAULT '{}',
    covering_prefix  cidr,
    peer             text NOT NULL DEFAULT '',
    as_path          bigint[] NOT NULL DEFAULT '{}',
    detail           text NOT NULL DEFAULT '',
    source           text NOT NULL DEFAULT 'lab'
);
ALTER TABLE alerts ADD COLUMN IF NOT EXISTS source text NOT NULL DEFAULT 'lab';
ALTER TABLE alerts ALTER COLUMN observed_origin TYPE bigint, ALTER COLUMN expected_origins TYPE bigint[], ALTER COLUMN as_path TYPE bigint[];
CREATE INDEX IF NOT EXISTS alerts_prefix_gist ON alerts USING gist (prefix inet_ops);
CREATE INDEX IF NOT EXISTS alerts_raised_at ON alerts (raised_at DESC);
CREATE INDEX IF NOT EXISTS alerts_source ON alerts (source);
