# The Elin Together depot server in a container (a Synology or any Docker host). The world lives in the volume.
#   docker build -t elin-depot server/
#   docker run -d --restart unless-stopped -p 55557:55557 -v /volume1/elin-depot:/depot -e DEPOT_PASSWORD=secret elin-depot
FROM python:3.12-alpine
COPY depot_server.py /app/depot_server.py
ENV DEPOT_DIR=/depot DEPOT_PORT=55557 PYTHONUNBUFFERED=1
VOLUME /depot
EXPOSE 55557
CMD ["python", "/app/depot_server.py"]
