import time

import yt_dlp

_cache = {}
CACHE_TTL = 300  # 5 minutes


def sanitize_pipes(text):
    if not text:
        return 'Unknown'
    return text.replace('|', '-').replace('\n', ' ')


def search_youtube(query, max_results=5):
    ydl_opts = {
        'quiet': True,
        'no_warnings': True,
        'extract_flat': True,
        'default_search': 'ytsearch',
    }
    with yt_dlp.YoutubeDL(ydl_opts) as ydl:
        result = ydl.extract_info(f'ytsearch{max_results}:{query}', download=False)
        entries = result.get('entries', [])
        return [
            {
                'id': e.get('id', ''),
                'title': sanitize_pipes(e.get('title', 'Unknown')),
                'channel': sanitize_pipes(e.get('channel', e.get('uploader', 'Unknown'))),
                'duration': e.get('duration', 0) or 0,
                'url': f"https://www.youtube.com/watch?v={e['id']}",
            }
            for e in entries if e.get('id')
        ]


def search_youtube_cached(query, max_results=5):
    key = f'{query}:{max_results}'
    now = time.time()
    if key in _cache and now - _cache[key][0] < CACHE_TTL:
        return _cache[key][1]
    results = search_youtube(query, max_results)
    _cache[key] = (now, results)
    return results
