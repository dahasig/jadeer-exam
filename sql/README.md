# SQL scripts (MySQL 8)

| File | Purpose |
|------|---------|
| `00_create_db_and_user.sql` | Create database + app user (change `CHANGE_ME`) |
| `01_schema.sql` | Full table schema matching EF Core `AppDbContext` |

## Apply

```bash
mysql -u root -p < sql/00_create_db_and_user.sql
mysql -u root -p < sql/01_schema.sql
```

Or from MySQL Workbench: open each file and execute.

The app can also create tables automatically via `EnsureCreated()` on first run if the database exists and is empty.
