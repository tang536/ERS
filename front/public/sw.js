/* 电租通 PWA Service Worker：只缓存应用外壳，业务接口一律走网络，避免读到过期订单与金额。 */
const SHELL_CACHE = 'erental-shell-v1'
const SHELL_URLS = ['/', '/favicon.svg', '/manifest.webmanifest']

self.addEventListener('install', event => {
  event.waitUntil(
    caches.open(SHELL_CACHE)
      .then(cache => cache.addAll(SHELL_URLS))
      .then(() => self.skipWaiting())
  )
})

self.addEventListener('activate', event => {
  event.waitUntil(
    caches.keys()
      .then(keys => Promise.all(keys.filter(k => k !== SHELL_CACHE).map(k => caches.delete(k))))
      .then(() => self.clients.claim())
  )
})

self.addEventListener('fetch', event => {
  const req = event.request
  if (req.method !== 'GET') return
  const url = new URL(req.url)

  // 业务接口、上传文件、第三方请求不缓存：订单状态和金额必须实时
  if (url.origin !== self.location.origin || url.pathname.startsWith('/api/') || url.pathname.startsWith('/uploads/')) return

  // 页面导航：网络优先，断网时回退到缓存的首页外壳
  if (req.mode === 'navigate') {
    event.respondWith(
      fetch(req).catch(() => caches.match('/').then(r => r || Response.error()))
    )
    return
  }

  // 静态资源：缓存优先并后台更新
  event.respondWith(
    caches.match(req).then(cached => {
      const network = fetch(req).then(res => {
        if (res.ok && res.type === 'basic') {
          const copy = res.clone()
          caches.open(SHELL_CACHE).then(cache => cache.put(req, copy))
        }
        return res
      }).catch(() => cached || Response.error())
      return cached || network
    })
  )
})
