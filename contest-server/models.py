from database import get_db


# --- Shows ---

def create_show(title, date):
    db = get_db()
    cursor = db.execute(
        'INSERT INTO shows (title, date) VALUES (?, ?)',
        (title, date),
    )
    db.commit()
    return cursor.lastrowid


def get_show(show_id):
    db = get_db()
    row = db.execute('SELECT * FROM shows WHERE id = ?', (show_id,)).fetchone()
    return dict(row) if row else None


def list_shows():
    db = get_db()
    rows = db.execute('SELECT * FROM shows ORDER BY created_at DESC').fetchall()
    return [dict(r) for r in rows]


def update_show_status(show_id, status):
    db = get_db()
    db.execute('UPDATE shows SET status = ? WHERE id = ?', (status, show_id))
    db.commit()


# --- Contestants ---

def get_or_create_contestant(display_name):
    db = get_db()
    row = db.execute(
        'SELECT * FROM contestants WHERE display_name = ?', (display_name,)
    ).fetchone()
    if row:
        return dict(row)
    cursor = db.execute(
        'INSERT INTO contestants (display_name) VALUES (?)', (display_name,)
    )
    db.commit()
    return {'id': cursor.lastrowid, 'display_name': display_name}


def add_contestant_to_show(show_id, contestant_id, slot_order=0):
    db = get_db()
    db.execute(
        'INSERT INTO show_contestants (show_id, contestant_id, slot_order) VALUES (?, ?, ?)',
        (show_id, contestant_id, slot_order),
    )
    db.commit()


def remove_contestant_from_show(show_id, contestant_id):
    db = get_db()
    db.execute(
        'DELETE FROM show_contestants WHERE show_id = ? AND contestant_id = ?',
        (show_id, contestant_id),
    )
    db.commit()


def get_show_contestants(show_id):
    db = get_db()
    rows = db.execute(
        '''SELECT c.id, c.display_name, sc.slot_order, sc.status
           FROM show_contestants sc
           JOIN contestants c ON sc.contestant_id = c.id
           WHERE sc.show_id = ? AND sc.status = 'active'
           ORDER BY sc.slot_order''',
        (show_id,),
    ).fetchall()
    return [dict(r) for r in rows]


def eliminate_contestant(show_id, contestant_id):
    db = get_db()
    db.execute(
        "UPDATE show_contestants SET status = 'eliminated' WHERE show_id = ? AND contestant_id = ?",
        (show_id, contestant_id),
    )
    db.commit()


# --- Rounds ---

def create_round(show_id, label=None):
    db = get_db()
    row = db.execute(
        'SELECT COALESCE(MAX(round_number), 0) + 1 AS next_num FROM rounds WHERE show_id = ?',
        (show_id,),
    ).fetchone()
    next_num = row['next_num']
    cursor = db.execute(
        'INSERT INTO rounds (show_id, round_number, label) VALUES (?, ?, ?)',
        (show_id, next_num, label),
    )
    db.commit()
    return cursor.lastrowid


def get_round(round_id):
    db = get_db()
    row = db.execute('SELECT * FROM rounds WHERE id = ?', (round_id,)).fetchone()
    return dict(row) if row else None


def get_round_by_number(show_id, round_number):
    db = get_db()
    row = db.execute(
        'SELECT * FROM rounds WHERE show_id = ? AND round_number = ?',
        (show_id, round_number),
    ).fetchone()
    return dict(row) if row else None


def get_current_round(show_id):
    db = get_db()
    row = db.execute(
        'SELECT * FROM rounds WHERE show_id = ? ORDER BY round_number DESC LIMIT 1',
        (show_id,),
    ).fetchone()
    return dict(row) if row else None


def get_show_rounds(show_id):
    db = get_db()
    rows = db.execute(
        'SELECT * FROM rounds WHERE show_id = ? ORDER BY round_number',
        (show_id,),
    ).fetchall()
    return [dict(r) for r in rows]


def set_round_status(round_id, status):
    db = get_db()
    db.execute('UPDATE rounds SET status = ? WHERE id = ?', (status, round_id))
    db.commit()


# --- Votes ---

def cast_vote(round_id, contestant_id, voter_name):
    db = get_db()
    rnd = db.execute('SELECT status FROM rounds WHERE id = ?', (round_id,)).fetchone()
    if not rnd or rnd['status'] != 'voting':
        return 'voting_closed'

    existing = db.execute(
        'SELECT id FROM votes WHERE round_id = ? AND voter_name = ?',
        (round_id, voter_name),
    ).fetchone()
    if existing:
        return 'already_voted'

    db.execute(
        'INSERT INTO votes (round_id, contestant_id, voter_name) VALUES (?, ?, ?)',
        (round_id, contestant_id, voter_name),
    )
    db.commit()
    return 'ok'


def get_round_results(round_id):
    db = get_db()
    rows = db.execute(
        '''SELECT c.id AS contestant_id, c.display_name, COUNT(v.id) AS vote_count
           FROM votes v
           JOIN contestants c ON v.contestant_id = c.id
           WHERE v.round_id = ?
           GROUP BY c.id
           ORDER BY vote_count DESC''',
        (round_id,),
    ).fetchall()
    return [dict(r) for r in rows]


def get_leaderboard(show_id):
    db = get_db()
    rows = db.execute(
        'SELECT * FROM leaderboard WHERE show_id = ?', (show_id,)
    ).fetchall()
    return [dict(r) for r in rows]
