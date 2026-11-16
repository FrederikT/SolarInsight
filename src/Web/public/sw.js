// Minimal PWA service worker for Solar Insight.
// Never caches /api/ or /health/ requests so private live data, CSRF tokens and session state stay strictly real-time.
const CACHE = 'solar-insight-shell-v1'

self.addEventListener('install', () => {
  self.skipWaiting()
})

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys().then(keys => Promise.all(keys.filter(k => k !== CACHE).map(k => caches.delete(k))))
      .then(() => self.clients.claim()),
  )
})

self.addEventListener('fetch', event => {
  const req = event.request
  if (req.method !== 'GET') return
  const url = new URL(req.url)
  if (url.origin !== self.location.origin) return
  if (url.pathname.includes('/api/') || url.pathname.includes('/health/')) return

  event.respondWith(
    fetch(req).then(res => {
      if (res.ok && (url.pathname.endsWith('.png') || url.pathname.endsWith('.ico') || url.pathname.endsWith('.css') || url.pathname.endsWith('.js'))) {
        const copy = res.clone()
        void caches.open(CACHE).then(cache => cache.put(req, copy))
      }
      return res
    }).catch(() => caches.match(req)),
  )
})
