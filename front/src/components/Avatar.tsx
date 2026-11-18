import { useEffect, useState } from 'react'

interface AvatarProps {
  /** 取首字符的来源（优先真实姓名，其次登录账号） */
  name?: string
  /** 换图后自增：同时用于破浏览器缓存与清除失败标记 */
  version?: number
  /** 容器附加类名，尺寸与配色仍由各自的 CSS 决定 */
  className?: string
  /** 头像地址（数据库 avatar_url）；空值表示没有上传过 */
  src?: string
  /** 传入后头像可点击/键盘打开大图，回调收到当前有效的图片地址 */
  onOpen?: (src: string) => void
  /** 真实图片加载成功时回调；用于页面提示「可查看大图」 */
  onLoaded?: () => void
}

/** 统一追加版本号：同 URL 换图后不会继续命中浏览器旧缓存 */
function withVersion(url: string, version: number): string {
  return `${url}${url.includes('?') ? '&' : '?'}v=${version}`
}

/**
 * 头像：**首字符打底 + 真实图片覆盖**。
 *
 * 头像地址来自数据库 avatar_url（由上传接口返回的 /uploads/avatars/… 地址），
 * 前端不再按 userId / userType 拼文件名，也不再轮询扩展名。
 * 没有地址或图片加载失败时，露出底下常驻的首字符，不会出现破图。
 */
export default function Avatar({
  name, version = 0, className = '', src, onOpen, onLoaded,
}: AvatarProps) {
  const [broken, setBroken] = useState(false)

  // 换图后推进 version：此前记下的加载失败状态必须清掉，否则新图永远不会显示
  useEffect(() => {
    setBroken(false)
  }, [version, src])

  const initial = ((name ?? '').trim().charAt(0) || 'U').toUpperCase()
  const explicit = src?.trim()
  const url = explicit ? withVersion(explicit, version) : ''
  const showImg = !!url && !broken
  const openable = showImg && !!onOpen

  const open = () => {
    if (openable && url) onOpen?.(url)
  }

  return (
    <div
      className={`avatar-box ${className} ${openable ? 'is-zoomable' : ''}`.trim()}
      onClick={openable ? open : undefined}
      onKeyDown={openable ? e => {
        if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); open() }
      } : undefined}
      role={openable ? 'button' : undefined}
      tabIndex={openable ? 0 : undefined}
      aria-label={openable ? '查看头像大图' : undefined}
      title={openable ? '点击查看头像大图' : undefined}
    >
      <span className="avatar-initial" aria-label="默认头像" aria-hidden={showImg || undefined}>
        {initial}
      </span>
      {showImg && (
        <img className="avatar-img" src={url} alt="头像" onLoad={onLoaded} onError={() => setBroken(true)} />
      )}
    </div>
  )
}
