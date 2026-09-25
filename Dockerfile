FROM python:3.10-slim

# Set environment variables to prevent python from writing pyc files and buffering stdout
ENV PYTHONDONTWRITEBYTECODE=1
ENV PYTHONUNBUFFERED=1

WORKDIR /app

# Install dependencies first for Docker layer caching
COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy the entire backend into the container
COPY backend/ ./backend/

# Cloud Run assigns the PORT environment variable (default 8080)
ENV PORT=8080
EXPOSE 8080

# Run the backend main loop
CMD ["python", "backend/main.py"]
