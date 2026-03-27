from flask import Blueprint, request

from youtube_search import search_youtube_cached

bp = Blueprint('youtube', __name__)


@bp.route('/search')
def youtube_search():
    query = request.args.get('q', '').strip()
    if not query:
        return 'ERR|missing_query'

    max_results = request.args.get('max', 5, type=int)
    max_results = min(max_results, 10)

    try:
        results = search_youtube_cached(query, max_results)
        lines = [f'OK|{len(results)}']
        for r in results:
            lines.append(f"{r['id']}|{r['title']}|{r['channel']}|{r['duration']}|{r['url']}")
        return '\n'.join(lines)
    except Exception:
        return 'ERR|search_failed'
