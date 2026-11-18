/**
 * 注册密码强度提示。
 *
 * 后端 Validators.IsStrongPassword 是权威（≥8 位且同时含字母与数字），
 * 这里不另立一套更严格的“硬门槛”，只把同一句话拆成可勾选的清单，
 * 并在用户输入时给出弱/中/强三档反馈；
 * 强度评分多出的大小写与符号属于“加分项”，不会阻止提交。
 */
export default function PasswordStrength({ password }: { password: string }) {
  const hasLength = password.length >= 8
  const hasLetter = /[A-Za-z]/.test(password)
  const hasDigit = /\d/.test(password)
  const hasMixedCase = /[a-z]/.test(password) && /[A-Z]/.test(password)
  const hasSymbol = /[^A-Za-z0-9]/.test(password)

  // 0-5 分：长度、字母、数字是底线 3 分；大小写混合、符号各加 1 分
  const score = (hasLength ? 1 : 0) + (hasLetter ? 1 : 0) + (hasDigit ? 1 : 0)
    + (hasMixedCase ? 1 : 0) + (hasSymbol ? 1 : 0)
  const level = score <= 2 ? 0 : score <= 4 ? 1 : 2
  const label = ['弱', '中', '强'][level]
  const tone = ['weak', 'medium', 'strong'][level]

  return (
    <div className={`pwd-strength is-${tone}`}>
      <div className="pwd-strength-head">
        <span>密码强度</span>
        <strong>{password ? label : '—'}</strong>
      </div>
      <div className="pwd-bars" aria-hidden="true">
        {[0, 1, 2].map(i => (
          <span key={i} className={i <= level && password ? `bar ${tone}` : 'bar'} />
        ))}
      </div>
      <ul className="pwd-rules">
        <li className={hasLength ? 'ok' : ''}>至少 8 位</li>
        <li className={hasLetter ? 'ok' : ''}>包含字母</li>
        <li className={hasDigit ? 'ok' : ''}>包含数字</li>
        <li className={hasMixedCase ? 'ok' : 'optional'}>大小写混合（加分）</li>
        <li className={hasSymbol ? 'ok' : 'optional'}>含符号（加分）</li>
      </ul>
    </div>
  )
}
