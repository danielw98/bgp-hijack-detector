# Dashboard image: FastAPI serving the alerts/routes UI and the lab control.
# Build context is the repo root.
FROM python:3.12-slim

WORKDIR /app

COPY dashboard/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY dashboard/app.py .
COPY dashboard/templates ./templates

ENV PYTHONUNBUFFERED=1

CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8000"]
