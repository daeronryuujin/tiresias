import os
import sqlite3

import click
from flask import current_app, g


def get_db():
    if 'db' not in g:
        g.db = sqlite3.connect(
            current_app.config['DATABASE'],
            detect_types=sqlite3.PARSE_DECLTYPES,
        )
        g.db.row_factory = sqlite3.Row
        g.db.execute('PRAGMA journal_mode=WAL')
        g.db.execute('PRAGMA foreign_keys=ON')
    return g.db


def close_db(e=None):
    db = g.pop('db', None)
    if db is not None:
        db.close()


def init_db():
    db = get_db()
    schema_path = os.path.join(os.path.dirname(__file__), 'schema.sql')
    with open(schema_path, 'r') as f:
        db.executescript(f.read())


def init_app(app):
    app.teardown_appcontext(close_db)

    @app.cli.command('init-db')
    def init_db_command():
        init_db()
        click.echo('Database initialized.')
