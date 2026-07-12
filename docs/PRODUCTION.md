# Production Deployment Guide

**HeroScript API** - REST API for headless card roguelike engine

---

## Overview

HeroScript is designed as a **REST API service** that game clients consume over HTTP. This architecture enables:

- **Hot-reload** of game configs without restarting clients
- **Multiple clients** (Unity, Godot, Web) sharing the same engine
- **Web-based tools** (dashboards, editors) running alongside gameplay
- **Headless simulations** for balancing and testing
- **Future multiplayer/cloud** support

**Latency:** 5-8ms on localhost (imperceptible for turn-based card games)

---

## Prerequisites

### Minimum Requirements
- **.NET 10 SDK/Runtime**
- **512MB RAM** (1GB+ recommended)
- **1GB disk space** (more for event logs over time)
- **Linux or Windows Server**

### Recommended for Production
- **Reverse proxy** (nginx, Caddy) for HTTPS
- **Systemd** (Linux) or **Windows Service** for process management
- **Backup strategy** for `data/events/` and `data/runs/`

---

## Deployment Options

### Option A: Docker (Recommended)

Easiest and most portable deployment method.

#### Quick Start

```bash
# 1. Build image
docker build -t heroscript-api .

# 2. Run container
docker run -d \
  --name heroscript \
  -p 8080:8080 \
  -e HERESCRIPT_ADMIN_KEY="your-secret-key-here" \
  -e AllowedOrigins__0="https://yourgame.com" \
  -v $(pwd)/data:/app/data \
  heroscript-api

# 3. Check health
curl http://localhost:8080/api/health
```

#### Using Docker Compose

```bash
# 1. Configure environment
cp .env.example .env
# Edit .env with your settings

# 2. Start services
docker-compose up -d

# 3. View logs
docker-compose logs -f

# 4. Stop services
docker-compose down
```

**Volumes mounted:**
- `./data/events` → Event sourcing logs (append-only)
- `./data/runs` → Run state snapshots
- `./data/configs` → Game configuration files

---

### Option B: Systemd Service (Linux)

For bare-metal Linux servers.

#### 1. Build and Deploy

```bash
# Build release
cd src/API
dotnet publish -c Release -o /opt/heroscript

# Copy configs
cp -r data/configs /opt/heroscript/data/
mkdir -p /opt/heroscript/data/events
mkdir -p /opt/heroscript/data/runs
```

#### 2. Create Service User

```bash
sudo useradd -r -s /bin/false heroscript
sudo chown -R heroscript:heroscript /opt/heroscript
```

#### 3. Create Systemd Service

Create `/etc/systemd/system/heroscript.service`:

```ini
[Unit]
Description=HeroScript API
After=network.target

[Service]
Type=notify
WorkingDirectory=/opt/heroscript
ExecStart=/usr/bin/dotnet /opt/heroscript/API.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
User=heroscript
Group=heroscript

# Environment variables
Environment="ASPNETCORE_ENVIRONMENT=Production"
Environment="ASPNETCORE_URLS=http://localhost:5260"
Environment="HERESCRIPT_ADMIN_KEY=your-secret-key-here"

# Security hardening
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=strict
ProtectHome=true
ReadWritePaths=/opt/heroscript/data

[Install]
WantedBy=multi-user.target
```

#### 4. Enable and Start

```bash
sudo systemctl daemon-reload
sudo systemctl enable heroscript
sudo systemctl start heroscript
sudo systemctl status heroscript
```

#### 5. View Logs

```bash
sudo journalctl -u heroscript -f
```

---

### Option C: Windows Service

For Windows Server deployments.

#### 1. Build Release

```powershell
cd src\API
dotnet publish -c Release -o C:\HeroScript
```

#### 2. Install as Windows Service

```powershell
# Using NSSM (Non-Sucking Service Manager)
# Download from: https://nssm.cc/download

nssm install HeroScript "C:\Program Files\dotnet\dotnet.exe"
nssm set HeroScript AppDirectory "C:\HeroScript"
nssm set HeroScript AppParameters "C:\HeroScript\API.dll"
nssm set HeroScript AppEnvironmentExtra ASPNETCORE_ENVIRONMENT=Production HERESCRIPT_ADMIN_KEY=your-secret-key-here

nssm start HeroScript
```

---

## Reverse Proxy Configuration

**Why?** Enable HTTPS, custom domain, rate limiting, and static file serving.

### Nginx Configuration

Create `/etc/nginx/sites-available/heroscript`:

```nginx
server {
    listen 80;
    server_name api.yourgame.com;
    
    # Redirect HTTP to HTTPS
    return 301 https://$server_name$request_uri;
}

server {
    listen 443 ssl http2;
    server_name api.yourgame.com;

    # SSL certificates (Let's Encrypt recommended)
    ssl_certificate /etc/letsencrypt/live/api.yourgame.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/api.yourgame.com/privkey.pem;
    
    # SSL hardening
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers HIGH:!aNULL:!MD5;
    ssl_prefer_server_ciphers on;

    # Proxy to HeroScript API
    location / {
        proxy_pass http://localhost:5260;
        proxy_http_version 1.1;
        
        # Forward real client IP
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header Host $host;
        
        # WebSocket support (future)
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection 'upgrade';
        proxy_cache_bypass $http_upgrade;
        
        # Timeouts
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 60s;
    }

    # Rate limiting (optional)
    limit_req_zone $binary_remote_addr zone=api_limit:10m rate=10r/s;
    limit_req zone=api_limit burst=20 nodelay;

    # Access logs
    access_log /var/log/nginx/heroscript-access.log;
    error_log /var/log/nginx/heroscript-error.log;
}
```

#### Enable and Reload

```bash
sudo ln -s /etc/nginx/sites-available/heroscript /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
```

#### SSL with Let's Encrypt

```bash
sudo apt install certbot python3-certbot-nginx
sudo certbot --nginx -d api.yourgame.com
```

---

## Environment Variables

### Required Configuration

| Variable | Description | Example |
|----------|-------------|---------|
| `ASPNETCORE_ENVIRONMENT` | Deployment environment | `Production` |
| `ASPNETCORE_URLS` | Bind address | `http://localhost:5260` |
| `HERESCRIPT_ADMIN_KEY` | Admin API key (CRUD endpoints) | `your-secret-key-here` |

### Optional Configuration

| Variable | Description | Default |
|----------|-------------|---------|
| `AllowedOrigins__0` | CORS allowed origin | `http://localhost:3000` |
| `AllowedOrigins__1` | Additional CORS origin | `http://localhost:5173` |
| `Persistence__EventStorePath` | Event logs directory | `data/events/` |
| `Persistence__RunStatePath` | Run snapshots directory | `data/runs/` |
| `Admin__Enabled` | Enable admin endpoints | `true` (dev), `false` (prod) |

### Configuration Methods

**Via Environment Variables (Recommended)**
```bash
export HERESCRIPT_ADMIN_KEY="your-secret-key-here"
export AllowedOrigins__0="https://yourgame.com"
```

**Via .env File (Docker)**
```bash
# .env
HERESCRIPT_ADMIN_KEY=your-secret-key-here
AllowedOrigins__0=https://yourgame.com
```

**Via appsettings.Production.json (Not Recommended)**
```json
{
  "Admin": {
    "ApiKey": "DO-NOT-COMMIT-SECRETS-HERE"
  }
}
```

---

## Security Checklist

### Pre-Deployment

- [ ] **Admin API Key set via environment variable** (not in `appsettings.json`)
- [ ] **`Admin:Enabled=false`** in `appsettings.Production.json` (if not using CRUD endpoints)
- [ ] **HTTPS enabled** via reverse proxy (nginx/Caddy)
- [ ] **`AllowedOrigins` restricted** to your domain(s) (no `*` wildcard)
- [ ] **Firewall configured** (only port 443 exposed publicly)
- [ ] **Strong admin key** (32+ characters, random)

### Post-Deployment

- [ ] **Health endpoint responding** (`GET /api/health`)
- [ ] **Admin endpoints require X-Admin-Key** (`POST /api/actions/reload` returns 401 without header)
- [ ] **CORS working** (frontend can access API)
- [ ] **Logs are structured** (check `journalctl` or `docker logs`)
- [ ] **Backup configured** for `data/events/` and `data/runs/`

---

## Backup & Recovery

### What to Backup

1. **Event Store** (`data/events/*.jsonl`)
   - Append-only event logs
   - Auditability and event replay
   - Use **incremental backup** (only new events)

2. **Run State** (`data/runs/*.json`)
   - Run snapshots for restart-safety
   - Use **full backup** (files are small)

3. **Configuration** (`data/configs/`)
   - Game definitions (actions, entities, etc)
   - Version control recommended (Git)

### Backup Strategy

**Daily Incremental (Events)**
```bash
# Rsync new events to backup server
rsync -avz --append-verify \
  /opt/heroscript/data/events/ \
  backup-server:/backups/heroscript/events/$(date +%Y%m%d)/
```

**Daily Full (Run State)**
```bash
# Copy run snapshots
rsync -avz --delete \
  /opt/heroscript/data/runs/ \
  backup-server:/backups/heroscript/runs/$(date +%Y%m%d)/
```

**Retention Policy**
- Events: 30 days (or longer for compliance)
- Run state: 7 days
- Configs: version controlled (Git)

### Recovery Procedure

```bash
# 1. Stop service
sudo systemctl stop heroscript

# 2. Restore from backup
rsync -avz backup-server:/backups/heroscript/events/20260712/ /opt/heroscript/data/events/
rsync -avz backup-server:/backups/heroscript/runs/20260712/ /opt/heroscript/data/runs/

# 3. Fix permissions
sudo chown -R heroscript:heroscript /opt/heroscript/data

# 4. Start service
sudo systemctl start heroscript

# 5. Verify
curl http://localhost:5260/api/health
```

---

## Monitoring & Observability

### Health Check Endpoint

```bash
# Basic health check
curl http://localhost:5260/api/health

# Expected response
{
  "status": "Healthy",
  "timestamp": "2026-07-12T18:30:00Z"
}
```

### Structured Logging

HeroScript uses structured logging with correlation IDs:

```json
{
  "Timestamp": "2026-07-12T18:30:00Z",
  "Level": "Information",
  "MessageTemplate": "Combat {CombatId} started",
  "Properties": {
    "CombatId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "CorrelationId": "abc123"
  }
}
```

**View logs:**
```bash
# Systemd
sudo journalctl -u heroscript -f

# Docker
docker logs -f heroscript

# Docker Compose
docker-compose logs -f
```

### Monitoring Recommendations

**Disk Space**
```bash
# Check event log growth
du -sh /opt/heroscript/data/events/

# Alert when >80% full
df -h /opt/heroscript/data | awk 'NR==2 {print $5}' | sed 's/%//'
```

**Error Rate**
```bash
# Count errors in last hour
sudo journalctl -u heroscript --since "1 hour ago" | grep -c "Level.*Error"
```

**API Response Time** (via nginx logs)
```bash
# Parse nginx access logs for slow requests (>1s)
awk '$NF > 1.0' /var/log/nginx/heroscript-access.log
```

---

## Troubleshooting

### API Not Responding

**Symptom:** `curl: (7) Failed to connect`

**Check:**
```bash
# Is service running?
sudo systemctl status heroscript

# Is port listening?
sudo netstat -tlnp | grep 5260

# Check logs for errors
sudo journalctl -u heroscript --since "5 minutes ago"
```

**Fix:**
```bash
# Restart service
sudo systemctl restart heroscript
```

---

### 401 Unauthorized on Admin Endpoints

**Symptom:** `POST /api/actions/reload` returns `401 Unauthorized`

**Cause:** Missing or incorrect `X-Admin-Key` header

**Fix:**
```bash
# Check if admin key is configured
sudo systemctl show heroscript | grep HERESCRIPT_ADMIN_KEY

# Test with correct key
curl -X POST http://localhost:5260/api/actions/reload \
  -H "X-Admin-Key: your-secret-key-here"
```

---

### CORS Errors in Browser

**Symptom:** `Access to fetch at 'http://api.yourgame.com' has been blocked by CORS policy`

**Cause:** `AllowedOrigins` not configured for your frontend domain

**Fix:**
```bash
# Add your domain to allowed origins
export AllowedOrigins__0="https://yourgame.com"

# Restart service
sudo systemctl restart heroscript
```

**Verify:**
```bash
# Check CORS headers
curl -H "Origin: https://yourgame.com" \
  -H "Access-Control-Request-Method: GET" \
  -X OPTIONS \
  http://localhost:5260/api/actions -v
```

---

### Out of Disk Space

**Symptom:** Logs show `IOException: No space left on device`

**Cause:** Event logs (`data/events/*.jsonl`) growing over time

**Fix (Short-term):**
```bash
# Archive old events (older than 30 days)
find /opt/heroscript/data/events/ -name "*.jsonl" -mtime +30 -exec gzip {} \;

# Move archives to backup
mv /opt/heroscript/data/events/*.gz /backup/heroscript/events/
```

**Fix (Long-term):**
```bash
# Set up log rotation in cron
crontab -e

# Add daily job to archive and remove old events
0 2 * * * find /opt/heroscript/data/events/ -name "*.jsonl" -mtime +30 -exec gzip {} \; && mv /opt/heroscript/data/events/*.gz /backup/
```

---

## Updates & Rollback

### Zero-Downtime Update (Docker)

```bash
# 1. Pull new image
docker pull heroscript-api:latest

# 2. Start new container (different port temporarily)
docker run -d \
  --name heroscript-new \
  -p 8081:8080 \
  -e HERESCRIPT_ADMIN_KEY="$ADMIN_KEY" \
  -v $(pwd)/data:/app/data \
  heroscript-api:latest

# 3. Health check new container
curl http://localhost:8081/api/health

# 4. Update nginx upstream (or switch traffic)

# 5. Stop old container
docker stop heroscript

# 6. Remove old container
docker rm heroscript

# 7. Rename new container
docker rename heroscript-new heroscript
```

### Rollback Procedure

```bash
# 1. Stop current service
sudo systemctl stop heroscript

# 2. Restore previous version
sudo cp /backup/heroscript-v1.0.0/API.dll /opt/heroscript/

# 3. Start service
sudo systemctl start heroscript

# 4. Verify
curl http://localhost:5260/api/health
```

---

## Performance Tuning

### ASP.NET Core Settings

**appsettings.Production.json:**
```json
{
  "Kestrel": {
    "Limits": {
      "MaxConcurrentConnections": 100,
      "MaxRequestBodySize": 10485760
    }
  }
}
```

### Nginx Caching (Optional)

Cache static responses to reduce load:

```nginx
# Add to nginx config
proxy_cache_path /var/cache/nginx levels=1:2 keys_zone=heroscript_cache:10m inactive=60m;

location /api/actions {
    proxy_cache heroscript_cache;
    proxy_cache_valid 200 5m;
    proxy_cache_key "$request_uri";
}
```

---

## Scaling Considerations

### Horizontal Scaling (Future)

HeroScript API is **stateless** except for:
- Event store (shared file system or database)
- Run state (shared storage)

For multiple instances:
1. Use **shared storage** (NFS, S3) for `data/`
2. Load balancer (nginx, HAProxy) in front
3. Session affinity **not required** (stateless API)

### Vertical Scaling

- **CPU:** Minimal impact (logic is lightweight)
- **RAM:** 512MB sufficient for most loads
- **Disk I/O:** Most critical (event logs are append-only)

---

## Example: Complete Production Setup (Ubuntu 22.04 + Docker)

```bash
# 1. Install Docker
sudo apt update
sudo apt install -y docker.io docker-compose
sudo systemctl enable docker

# 2. Clone HeroScript
git clone https://github.com/your-org/heroscript.git
cd heroscript

# 3. Configure environment
cat > .env <<EOF
ASPNETCORE_ENVIRONMENT=Production
HERESCRIPT_ADMIN_KEY=$(openssl rand -hex 32)
AllowedOrigins__0=https://yourgame.com
EOF

# 4. Start services
docker-compose up -d

# 5. Install nginx
sudo apt install -y nginx certbot python3-certbot-nginx

# 6. Configure nginx (see Nginx Configuration section)

# 7. Get SSL certificate
sudo certbot --nginx -d api.yourgame.com

# 8. Verify
curl https://api.yourgame.com/api/health
```

---

## Further Reading

- [Client Integration Guide](CLIENT_INTEGRATION.md) - Consuming API from Unity/Godot/Web
- [Security Documentation](security.md) - AdminKey, CORS, best practices
- [Observability Guide](observability.md) - Logging, correlation IDs
- [Persistence Documentation](persistence.md) - Event store, run state

---

## Support

For issues or questions:
- GitHub Issues: `https://github.com/your-org/heroscript/issues`
- Documentation: `docs/README.md`
