#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# Contest Voting Server — Cloud Setup Script
# Run this on a fresh Ubuntu/Debian server: sudo bash setup.sh
# ============================================================================

APP_DIR="/opt/contest-server"
DATA_DIR="$APP_DIR/data"
VENV_DIR="$APP_DIR/venv"
SERVICE_USER="contest"
SERVICE_NAME="contest-server"
GUNICORN_BIND="127.0.0.1:8090"
GUNICORN_WORKERS=2

echo "=== Contest Server Setup ==="

# --- 1. Install system packages ---
echo "[1/8] Installing system packages..."
apt-get update -qq
apt-get install -y -qq python3 python3-venv python3-pip nginx certbot python3-certbot-nginx ufw

# --- 2. Create service user ---
echo "[2/8] Creating service user '$SERVICE_USER'..."
if ! id "$SERVICE_USER" &>/dev/null; then
    useradd --system --no-create-home --shell /usr/sbin/nologin "$SERVICE_USER"
fi

# --- 3. Copy application files ---
echo "[3/8] Setting up application directory..."
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
if [ "$SCRIPT_DIR" != "$APP_DIR" ]; then
    mkdir -p "$APP_DIR"
    cp -r "$SCRIPT_DIR"/* "$APP_DIR"/
fi

# --- 4. Create Python venv and install dependencies ---
echo "[4/8] Creating Python virtual environment..."
python3 -m venv "$VENV_DIR"
"$VENV_DIR/bin/pip" install --quiet --upgrade pip
"$VENV_DIR/bin/pip" install --quiet -r "$APP_DIR/requirements.txt"

# --- 5. Set up data directory and environment ---
echo "[5/8] Configuring database and environment..."
mkdir -p "$DATA_DIR"
chown -R "$SERVICE_USER":"$SERVICE_USER" "$DATA_DIR"

SECRET_KEY=$(python3 -c "import secrets; print(secrets.token_hex(32))")
cat > "$APP_DIR/.env" <<ENVEOF
CONTEST_DB=$DATA_DIR/contest.db
SECRET_KEY=$SECRET_KEY
FLASK_ENV=production
ENVEOF
chmod 600 "$APP_DIR/.env"
chown "$SERVICE_USER":"$SERVICE_USER" "$APP_DIR/.env"

# --- 6. Install systemd service ---
echo "[6/8] Installing systemd service..."
cat > "/etc/systemd/system/$SERVICE_NAME.service" <<SVCEOF
[Unit]
Description=Contest Voting Server
After=network.target

[Service]
Type=simple
User=$SERVICE_USER
WorkingDirectory=$APP_DIR
EnvironmentFile=$APP_DIR/.env
ExecStart=$VENV_DIR/bin/gunicorn -b $GUNICORN_BIND -w $GUNICORN_WORKERS "app:create_app()"
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
SVCEOF

systemctl daemon-reload
systemctl enable "$SERVICE_NAME"
systemctl start "$SERVICE_NAME"

# --- 7. Configure nginx ---
echo "[7/8] Configuring nginx..."

read -rp "Enter your domain name (or press Enter for default '_'): " DOMAIN
DOMAIN="${DOMAIN:-_}"

cat > "/etc/nginx/sites-available/contest" <<NGXEOF
server {
    listen 80;
    server_name $DOMAIN;

    location /world/ {
        proxy_pass http://$GUNICORN_BIND;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        add_header Access-Control-Allow-Origin *;
    }

    location / {
        proxy_pass http://$GUNICORN_BIND;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
    }
}
NGXEOF

ln -sf /etc/nginx/sites-available/contest /etc/nginx/sites-enabled/contest
rm -f /etc/nginx/sites-enabled/default
nginx -t
systemctl reload nginx

# --- 8. Firewall ---
echo "[8/8] Configuring firewall..."
ufw allow 'Nginx Full' >/dev/null 2>&1 || true
ufw allow OpenSSH >/dev/null 2>&1 || true
ufw --force enable >/dev/null 2>&1 || true

echo ""
echo "=== Setup Complete ==="
echo "Service status:  systemctl status $SERVICE_NAME"
echo "Nginx status:    systemctl status nginx"
echo "Logs:            journalctl -u $SERVICE_NAME -f"
echo ""

# --- Optional: HTTPS via certbot ---
if [ "$DOMAIN" != "_" ]; then
    read -rp "Set up HTTPS with Let's Encrypt for $DOMAIN? [y/N]: " SETUP_SSL
    if [[ "${SETUP_SSL,,}" == "y" ]]; then
        certbot --nginx -d "$DOMAIN" --non-interactive --agree-tos --register-unsafely-without-email
        echo "HTTPS configured successfully."
    fi
fi

echo "Done. Your contest server is running at http://$DOMAIN/"
