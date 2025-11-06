#!/bin/bash
# Fix SELinux context for SQLite database file

DB_FILE="/tmp/translarr.db"

if [ -f "$DB_FILE" ]; then
    echo "Fixing SELinux context for $DB_FILE"
    chcon -t container_file_t "$DB_FILE"
    echo "Done. File context: $(ls -Z $DB_FILE)"
else
    echo "Database file not found yet. Will be created by Aspire."
fi
