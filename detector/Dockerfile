# Collector image: exabgp peering with the lab, feeding the detector into Postgres,
# plus the C MRT parser and the recorded incidents for replay.
# Build context is the repo root.
FROM python:3.11-slim AS mrt

RUN apt-get update \
    && apt-get install -y --no-install-recommends gcc libc6-dev \
    && rm -rf /var/lib/apt/lists/*
COPY detector/mrt/mrt.c detector/mrt/mrt.h detector/mrt/build.sh /mrt/
RUN sh /mrt/build.sh

FROM python:3.11-slim

WORKDIR /opt/bgpwatch

COPY detector/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY detector/bgpwatch ./bgpwatch
COPY detector/mrt/mrt.h ./mrt/mrt.h
COPY --from=mrt /mrt/build/libmrt.so ./mrt/build/libmrt.so
COPY detector/incidents ./incidents
COPY detector/schema.sql detector/config.toml ./
COPY detector/run-process.sh .
COPY detector/exabgp.conf /etc/exabgp/exabgp.conf
RUN chmod +x run-process.sh

ENV PYTHONPATH=/opt/bgpwatch \
    PYTHONUNBUFFERED=1 \
    exabgp_daemon_user=root \
    exabgp_daemon_daemonize=false \
    exabgp_log_enable=true \
    exabgp_log_destination=stdout

CMD ["exabgp", "/etc/exabgp/exabgp.conf"]
