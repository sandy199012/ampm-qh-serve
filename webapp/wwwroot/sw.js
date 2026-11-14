// Minimal service worker: makes the site installable as an app (Chrome / Edge / Safari).
// It does not cache anything, so users always get the latest pages and data.
self.addEventListener('install', function () { self.skipWaiting(); });
self.addEventListener('activate', function (e) { e.waitUntil(self.clients.claim()); });
self.addEventListener('fetch', function () { /* network only */ });
