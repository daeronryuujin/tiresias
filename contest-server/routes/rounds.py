from flask import Blueprint, jsonify, redirect, render_template, request, url_for

from models import (
    create_round,
    get_round,
    get_round_results,
    get_show,
    get_show_contestants,
    set_round_status,
)

bp = Blueprint('rounds', __name__)


# --- JSON API ---

@bp.route('/api/shows/<int:show_id>/rounds', methods=['POST'])
def api_create_round(show_id):
    data = request.get_json(silent=True) or {}
    label = data.get('label')
    round_id = create_round(show_id, label=label)
    return jsonify({'id': round_id}), 201


@bp.route('/api/rounds/<int:round_id>', methods=['PUT'])
def api_update_round(round_id):
    data = request.get_json(force=True)
    status = data.get('status')
    if status:
        set_round_status(round_id, status)
    return jsonify({'ok': True})


@bp.route('/api/rounds/<int:round_id>/results', methods=['GET'])
def api_round_results(round_id):
    return jsonify(get_round_results(round_id))


# --- HTML Dashboard ---

@bp.route('/shows/<int:show_id>/rounds/create', methods=['POST'])
def create_round_page(show_id):
    label = request.form.get('label', '').strip() or None
    create_round(show_id, label=label)
    return redirect(url_for('shows.show_detail_page', show_id=show_id))


@bp.route('/rounds/<int:round_id>')
def round_detail_page(round_id):
    rnd = get_round(round_id)
    if not rnd:
        return 'Round not found', 404
    show = get_show(rnd['show_id'])
    contestants = get_show_contestants(rnd['show_id'])
    results = get_round_results(round_id)
    return render_template(
        'round_detail.html',
        round=rnd,
        show=show,
        contestants=contestants,
        results=results,
    )


@bp.route('/rounds/<int:round_id>/status', methods=['POST'])
def update_round_status_page(round_id):
    rnd = get_round(round_id)
    status = request.form.get('status', '').strip()
    if status:
        set_round_status(round_id, status)
    return redirect(url_for('rounds.round_detail_page', round_id=round_id))
