import os

from flask import Flask, redirect, url_for

import database


def create_app():
    app = Flask(__name__)
    app.config['DATABASE'] = os.environ.get('CONTEST_DB', os.path.join(app.instance_path, 'contest.db'))
    app.config['SECRET_KEY'] = os.environ.get('SECRET_KEY', 'dev-key-change-me')

    os.makedirs(os.path.dirname(app.config['DATABASE']), exist_ok=True)

    database.init_app(app)

    with app.app_context():
        database.init_db()

    from routes.world import bp as world_bp
    from routes.shows import bp as shows_bp
    from routes.contestants import bp as contestants_bp
    from routes.rounds import bp as rounds_bp
    from routes.youtube import bp as youtube_bp

    app.register_blueprint(world_bp, url_prefix='/world')
    app.register_blueprint(shows_bp)
    app.register_blueprint(contestants_bp)
    app.register_blueprint(rounds_bp)
    app.register_blueprint(youtube_bp, url_prefix='/world/youtube')

    @app.route('/')
    def index():
        return redirect(url_for('shows.list_shows_page'))

    return app


if __name__ == '__main__':
    app = create_app()
    app.run(debug=True)
