from sqlite3 import IntegrityError

from flask import Blueprint, request

from models import (
    add_contestant_to_show,
    cast_vote,
    get_current_round,
    get_leaderboard,
    get_or_create_contestant,
    get_round_by_number,
    get_round_results,
    get_show,
    get_show_contestants,
)

bp = Blueprint('world', __name__)


@bp.route('/<int:show_id>/state')
def world_state(show_id):
    show = get_show(show_id)
    if not show:
        return 'ERR|show_not_found'
    current = get_current_round(show_id)
    round_num = current['round_number'] if current else 0
    round_status = current['status'] if current else 'none'
    return f"OK|{show['status']}|{round_num}|{round_status}"


@bp.route('/<int:show_id>/contestants')
def world_contestants(show_id):
    rows = get_show_contestants(show_id)
    lines = [f'OK|{len(rows)}']
    for r in rows:
        lines.append(f"{r['id']}|{r['display_name']}")
    return '\n'.join(lines)


@bp.route('/<int:show_id>/signup')
def world_signup(show_id):
    name = request.args.get('name', '').strip()
    if not name:
        return 'ERR|missing_name'
    show = get_show(show_id)
    if not show or show['status'] == 'completed':
        return 'ERR|signups_closed'
    contestant = get_or_create_contestant(name)
    try:
        add_contestant_to_show(show_id, contestant['id'])
        return f"OK|signed_up|{contestant['id']}"
    except IntegrityError:
        return 'ERR|already_signed_up'


@bp.route('/<int:show_id>/vote')
def world_vote(show_id):
    round_num = request.args.get('round', type=int)
    contestant_id = request.args.get('contestant', type=int)
    voter = request.args.get('voter', '').strip()
    if not all([round_num, contestant_id, voter]):
        return 'ERR|missing_params'
    rnd = get_round_by_number(show_id, round_num)
    if not rnd:
        return 'ERR|round_not_found'
    result = cast_vote(rnd['id'], contestant_id, voter)
    if result == 'ok':
        return 'OK|voted'
    return f'ERR|{result}'


@bp.route('/<int:show_id>/results')
def world_results(show_id):
    round_num = request.args.get('round', type=int)
    rnd = get_round_by_number(show_id, round_num)
    if not rnd:
        return 'ERR|round_not_found'
    rows = get_round_results(rnd['id'])
    lines = [f'OK|{len(rows)}']
    for r in rows:
        lines.append(f"{r['contestant_id']}|{r['display_name']}|{r['vote_count']}")
    return '\n'.join(lines)


@bp.route('/<int:show_id>/leaderboard')
def world_leaderboard(show_id):
    rows = get_leaderboard(show_id)
    lines = [f'OK|{len(rows)}']
    for r in rows:
        lines.append(f"{r['contestant_id']}|{r['display_name']}|{r['total_votes']}|{r['rounds_played']}")
    return '\n'.join(lines)
