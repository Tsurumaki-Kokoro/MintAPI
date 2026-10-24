#!/usr/bin/env bash
set -euo pipefail
[[ ${EUID} == 0 ]] || { echo '请使用 sudo bash install.sh'; exit 1; }
[[ $(uname -m) == x86_64 ]] || { echo '此安装包仅支持 x86_64'; exit 1; }
source /etc/os-release
case "${ID}:${VERSION_ID}" in debian:12|debian:13|ubuntu:22.04|ubuntu:24.04|ubuntu:26.04) ;; *) echo '仅支持 Debian 12/13、Ubuntu 22.04/24.04/26.04'; exit 1;; esac
root=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
[[ $root != *' '* && $root != *$'\n'* && $root != *'%'* && $root != *'"'* ]] || { echo '请在无空格或特殊字符路径解压。'; exit 1; }
[[ -x $root/app/MintAPI ]] || { echo '请使用 Release 预编译包。'; exit 1; }
# The package carries a version-matched browser. Only shared OS libraries are installed here.
export PLAYWRIGHT_BROWSERS_PATH="$root/app/browsers"
node="$root/app/.playwright/node/linux-x64/node"
"$node" "$root/app/.playwright/package/cli.js" install-deps chromium
"$root/app/MintAPI" --self-test
id mintapi >/dev/null 2>&1 || useradd --system --home-dir /var/lib/mintapi --shell /usr/sbin/nologin mintapi
install -d -m 700 /var/lib/mintapi
# Writes config separately from replaceable application versions. Existing config is backed up by the wizard.
"$root/app/MintAPI" --setup --config /var/lib/mintapi/appsettings.Production.json
chown -R mintapi:mintapi /var/lib/mintapi
systemctl stop mintapi.service 2>/dev/null || true
release="/opt/mintapi/releases/$(date -u +%Y%m%dT%H%M%S)-$$"
install -d "$release"
cp -a "$root/app/." "$release/"
# Keep an old version for an operator-driven rollback; never roll back a migrated database automatically.
install -d /opt/mintapi
ln -sfn "$release" /opt/mintapi/current
cat > /etc/systemd/system/mintapi.service <<'UNIT'
[Unit]
Description=MintAPI
After=network-online.target
Wants=network-online.target
[Service]
User=mintapi
Group=mintapi
WorkingDirectory=/opt/mintapi/current
ExecStart=/opt/mintapi/current/MintAPI
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=MINTAPI_CONFIG_FILE=/var/lib/mintapi/appsettings.Production.json
Restart=on-failure
RestartSec=10
TimeoutStopSec=30
UMask=0077
[Install]
WantedBy=multi-user.target
UNIT
systemctl daemon-reload
systemctl enable --now mintapi.service
# Configuration can specify a different port; obtain the URL from the application, not a shell JSON parser.
/opt/mintapi/current/MintAPI --wait-ready --config /var/lib/mintapi/appsettings.Production.json
echo '安装完成。配置/API Key：/var/lib/mintapi/appsettings.Production.json；服务：systemctl status mintapi；日志：journalctl -u mintapi'
