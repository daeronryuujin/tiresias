from flask import Blueprint, jsonify, redirect, request, url_for

from models import (
    add_contestant_to_show,
    eliminate_contestant,
    get_or_create_contestant,
    get_show_contestants,
    remove_contestant_from_show,
)

bp = Blueprint('contestants', __name__)


# --- JSON API ---

@bp.route('/api/shows/<int:show_id>/contestants', methods=['GET'])
def api_list_contestants(show_id):
    return jsonify(get_show_contestants(show_id))


@bp.route('/api/shows/<int:show_id>/contestants', methods=['POST'])
def api_add_contestant(show_id):
    data = request.get_json(force=True)
    name = data.get('display_name', '').strip()
    if not name:
        return jsonify({'error': 'display_name required'}), 400
    contestant = get_or_create_contestant(name)
    add_contestant_to_show(show_id, contestant['id'])
    return jsonify({'id': contestant['id'], 'display_name': name}), 201


@bp.route('/api/shows/<int:show_id>/contestants/<int:contestant_id>', methods=['DELETE'])
def api_remove_contestant(show_id, contestant_id):
    remove_contestant_from_show(show_id, contestant_id)
    return jsonify({'ok': True})


# --- HTML Dashboard ---

@bp.route('/shows/<int:show_id>/contestants/add', methods=['POST'])
def add_contestant_page(show_id):
    name = request.form.get('display_name', '').strip()
    if name:
        contestant = get_or_create_contestant(name)
        add_contestant_to_show(show_id, contestant['id'])
    return redirect(url_for('shows.show_detail_page', show_id=show_id))


@bp.route('/shows/<int:show_id>/contestants/<int:contestant_id>/remove', methods=['POST'])
def remove_contestant_page(show_id, contestant_id):
    remove_contestant_from_show(show_id, contestant_id)
    return redirect(url_for('shows.show_detail_page', show_id=show_id))


@bp.route('/shows/<int:show_id>/contestants/<int:contestant_id>/eliminate', methods=['POST'])
def eliminate_contestant_page(show_id, contestant_id):
    eliminate_contestant(show_id, contestant_id)
    return redirect(url_for('shows.show_detail_page', show_id=show_id))
