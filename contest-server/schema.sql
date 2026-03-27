CREATE TABLE IF NOT EXISTS shows (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    title       TEXT NOT NULL,
    date        TEXT NOT NULL,
    status      TEXT NOT NULL DEFAULT 'upcoming',
    created_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS contestants (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    display_name    TEXT NOT NULL UNIQUE,
    created_at      TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE IF NOT EXISTS show_contestants (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    show_id         INTEGER NOT NULL REFERENCES shows(id),
    contestant_id   INTEGER NOT NULL REFERENCES contestants(id),
    slot_order      INTEGER NOT NULL DEFAULT 0,
    status          TEXT NOT NULL DEFAULT 'active',
    UNIQUE(show_id, contestant_id)
);

CREATE TABLE IF NOT EXISTS rounds (
    id           INTEGER PRIMARY KEY AUTOINCREMENT,
    show_id      INTEGER NOT NULL REFERENCES shows(id),
    round_number INTEGER NOT NULL,
    label        TEXT,
    status       TEXT NOT NULL DEFAULT 'pending',
    created_at   TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE(show_id, round_number)
);

CREATE TABLE IF NOT EXISTS votes (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    round_id        INTEGER NOT NULL REFERENCES rounds(id),
    contestant_id   INTEGER NOT NULL REFERENCES contestants(id),
    voter_name      TEXT NOT NULL,
    cast_at         TEXT NOT NULL DEFAULT (datetime('now')),
    UNIQUE(round_id, voter_name)
);

DROP VIEW IF EXISTS leaderboard;
CREATE VIEW leaderboard AS
SELECT
    s.id AS show_id,
    c.id AS contestant_id,
    c.display_name,
    COALESCE(SUM(v.vote_count), 0) AS total_votes,
    COUNT(DISTINCT r.id) AS rounds_played
FROM show_contestants sc
JOIN shows s ON sc.show_id = s.id
JOIN contestants c ON sc.contestant_id = c.id
LEFT JOIN rounds r ON r.show_id = s.id AND r.status = 'closed'
LEFT JOIN (
    SELECT round_id, contestant_id, COUNT(*) AS vote_count
    FROM votes GROUP BY round_id, contestant_id
) v ON v.round_id = r.id AND v.contestant_id = c.id
WHERE sc.status = 'active'
GROUP BY s.id, c.id
ORDER BY total_votes DESC;
