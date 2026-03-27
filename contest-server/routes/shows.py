from flask import Blueprint, jsonify, redirect, render_template, request, url_for

from models import (
    create_show,
    get_leaderboard,
    get_show,
    get_show_contestants,
    get_show_rounds,
    list_shows,
    update_show_status,
)

bp = Blueprint('shows', __name__)


# --- JSON API ---

@bp.route('/api/shows', methods=['GET'])
def api_list_shows():
    return jsonify(list_shows())


@bp.route('/api/shows', methods=['POST'])
def api_create_show():
    data = request.get_json(force=True)
    title = data.get('title', '').strip()
    date = data.get('date', '').strip()
    if not title or not date:
        return jsonify({'error': 'title and date required'}), 400
    show_id = create_show(title, date)
    return jsonify({'id': show_id}), 201


@bp.route('/api/shows/<int:show_id>', methods=['GET'])
def api_get_show(show_id):
    show = get_show(show_id)
    if not show:
        return jsonify({'error': 'not found'}), 404
    return jsonify(show)


@bp.route('/api/shows/<int:show_id>', methods=['PUT'])
def api_update_show(show_id):
    data = request.get_json(force=True)
    status = data.get('status')
    if status:
        update_show_status(show_id, status)
    return jsonify({'ok': True})


@bp.route('/api/shows/<int:show_id>/leaderboard', methods=['GET'])
def api_leaderboard(show_id):
    return jsonify(get_leaderboard(show_id))


# --- HTML Dashboard ---

@bp.route('/shows')
def list_shows_page():
    shows = list_shows()
    return render_template('shows.html', shows=shows)


@bp.route('/shows/create', methods=['POST'])
def create_show_page():
    title = request.form.get('title', '').strip()
    date = request.form.get('date', '').strip()
    if title and date:
        create_show(title, date)
    return redirect(url_for('shows.list_shows_page'))


@bp.route('/shows/<int:show_id>')
def show_detail_page(show_id):
    show = get_show(show_id)
    if not show:
        return 'Show not found', 404
    contestants = get_show_contestants(show_id)
    rounds = get_show_rounds(show_id)
    leaderboard = get_leaderboard(show_id)
    return render_template(
        'show_detail.html',
        show=show,
        contestants=contestants,
        rounds=rounds,
        leaderboard=leaderboard,
    )


@bp.route('/shows/<int:show_id>/status', methods=['POST'])
def update_show_status_page(show_id):
    status = request.form.get('status', '').strip()
    if status:
        update_show_status(show_id, status)
    return redirect(url_for('shows.show_detail_page', show_id=show_id))
