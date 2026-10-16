# AMPM IT Helpdesk - Mobile App

## Setup Steps

### Step 1: Flutter Install karo
1. https://flutter.dev/docs/get-started/install/windows
2. Extract to C:\flutter\
3. PATH add karo: C:\flutter\bin
4. CMD mein run karo: `flutter doctor`

### Step 2: Android Studio / SDK
- Flutter doctor mein sab green hona chahiye
- Android SDK install ho

### Step 3: Server Start karo
AMPMHelpdesk folder mein:
```
RUN_HELPDESK.bat
```
Server: http://0.0.0.0:5000

### Step 4: APK Build karo
```
BUILD_APK.bat
```
APK: build\app\outputs\flutter-apk\app-release.apk

### Step 5: Phone pe install
1. APK phone mein copy karo (USB/WhatsApp/Email)
2. Settings → Security → Unknown Sources ON
3. APK pe tap → Install

### Step 6: App Setup
1. App kholo
2. Server URL daalo: http://192.168.0.xxx:5000
3. Test Connection
4. Save & Open

## Features
- Dashboard — stats, recent tickets
- Ticket Raise — complete form
- Ticket List — filter by status
- Ticket Detail — update status, resolution
- Works on Android phones in office WiFi
