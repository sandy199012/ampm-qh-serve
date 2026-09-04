# AMPM QH Monitor Server - Render.com Deployment Guide

## Step by Step:

### 1. GitHub pe upload karo
```
1. github.com pe nayi repository banao: "ampm-qh-server"
2. Ye saari files upload karo
```

### 2. Render.com pe deploy karo
```
1. render.com pe login karo (free account)
2. "New +" → "Web Service"
3. GitHub repo connect karo
4. Settings:
   - Name: ampm-qh-monitor
   - Environment: Python 3
   - Build Command: pip install -r requirements.txt
   - Start Command: gunicorn wsgi:app --bind 0.0.0.0:$PORT --workers 1
5. "Create Web Service" click karo
```

### 3. AMPMTool mein server URL update karo
Render dega ek URL jaise:
```
https://ampm-qh-monitor.onrender.com
```
Ye URL AMPMTool ke EndpointMonitor tab mein daalo.

### 4. QHAgent mein bhi URL update karo
```
QHAgent_Source/server.txt ya server config mein
https://ampm-qh-monitor.onrender.com
```

## API Endpoints:
- GET  /api/summary      - Dashboard stats
- GET  /api/endpoints    - All endpoints list
- POST /api/report       - Agent se data receive karo
- GET  /api/licenses     - License list
- POST /api/licenses     - License add karo

## Note - Free Tier Limitation:
Render free tier pe server 15 min inactivity ke baad sleep ho jaata hai.
Pehle request aane pe 30-50 sec lag sakti hai wake up hone mein.
Paid tier lene pe ye problem nahi hogi.
