ARG MSSQL_IMAGE=mcr.microsoft.com/mssql/server:2025-latest
FROM ${MSSQL_IMAGE}
USER root
COPY --chmod=755 entrypoint.sh /usr/local/bin/tmusic-sql-entrypoint
ENTRYPOINT ["/usr/local/bin/tmusic-sql-entrypoint"]
CMD ["/opt/mssql/bin/sqlservr"]
