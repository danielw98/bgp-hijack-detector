# Collector image: exabgp peering with the lab, feeding the detector into Postgres.
# Build context is the repo root.
FROM python:3.11-slim

WORKDIR /opt/bgpwatch

COPY detector/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY detector/bgpwatch ./bgpwatch
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
