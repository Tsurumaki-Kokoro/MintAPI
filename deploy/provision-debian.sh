#!/usr/bin/env bash
set -euo pipefail
[[ ${EUID} == 0 ]] || { echo 'Run the installer as root.' >&2; exit 1; }
component=${1:?database or cache}
IFS= read -r password
[[ $password =~ ^[a-f0-9]{48}$ ]] || exit 1
export DEBIAN_FRONTEND=noninteractive
if [[ $component == database ]]; then
    # Do not reconfigure an existing database, even if currently stopped.
    if dpkg-query -W -f='${Status}' mariadb-server mysql-server 2>/dev/null | grep -q 'install ok installed'; then
        echo 'Database package already exists; select an existing connection.' >&2; exit 1
    fi
    if timeout 1 bash -c 'echo >/dev/tcp/127.0.0.1/3306' 2>/dev/null; then
        echo 'Database port is occupied; select an existing connection.' >&2; exit 1
    fi
    apt-get update
    apt-get install -y --no-install-recommends mariadb-server
    systemctl enable --now mariadb
    # Generated hex credentials are sent over stdin, never process arguments.
    mariadb --protocol=socket --user=root <<SQL
CREATE DATABASE mintapi CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER 'mintapi'@'localhost' IDENTIFIED BY '$password';
GRANT ALL PRIVILEGES ON mintapi.* TO 'mintapi'@'localhost';
CREATE USER 'mintapi'@'127.0.0.1' IDENTIFIED BY '$password';
GRANT ALL PRIVILEGES ON mintapi.* TO 'mintapi'@'127.0.0.1';
SQL
elif [[ $component == cache ]]; then
    if dpkg-query -W -f='${Status}' redis-server 2>/dev/null | grep -q 'install ok installed'; then
        echo 'Redis package already exists; select an existing connection.' >&2; exit 1
    fi
    if timeout 1 bash -c 'echo >/dev/tcp/127.0.0.1/6379' 2>/dev/null; then
        echo 'Cache port is occupied; select an existing connection.' >&2; exit 1
    fi
    apt-get update
    apt-get install -y --no-install-recommends redis-server
    cat >> /etc/redis/redis.conf <<CONF

# MintAPI: local-only authenticated cache.
bind 127.0.0.1 ::1
protected-mode yes
requirepass $password
CONF
    chmod 640 /etc/redis/redis.conf
    systemctl enable redis-server
    systemctl restart redis-server
else exit 1; fi
