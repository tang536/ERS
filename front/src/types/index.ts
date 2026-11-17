/** 系统角色（与后端 AuthService.Role* 常量一一对应） */
export type RoleName = '管理员' | '前台' | '财务' | '顾客'
/**
 * 账号类型（与后端 SystemUser.UserType 多态取值一一对应）。
 * 员工侧细分 admin / reception / finance，顾客侧为 customer；
 * 界面判定通常只关心「是不是顾客」，因此比较时一律与 'customer' 对照。
 */
export type UserType = 'customer' | 'staff' | 'admin' | 'reception' | 'finance'

export interface UserInfo {
  userType: UserType
  /** 系统角色，用于路由与菜单判定 */
  roleName: RoleName | string
  /** 会员等级（仅顾客） */
  memberLevel?: string
  userName: string
  userId: number
  /** 顾客租车资料（身份证号 + 手机号）是否已完善 */
  profileCompleted?: boolean
  /** 员工使用管理员发放的临时密码登录，必须先改密 */
  mustChangePassword?: boolean
  /** 临时密码失效时间（仅员工且 mustChangePassword 时有意义） */
  passwordResetExpire?: string
}

export interface Vehicle {
  vehicleId: number
  vehicleNo: string
  frameNumber?: string
  brand?: string
  buyTime?: string
  buyPrice?: number
  vehicleStatus: string
  /** 该车日租金；为空表示未单独定价，下单时回退系统配置 */
  dailyRent?: number | null
  /** 该车押金；为空表示未单独定价，下单时回退系统配置 */
  deposit?: number | null
  qrCode?: string
  /** 车辆图片相对路径（如 /uploads/vehicle/xxx.jpg），无图为 null */
  imageUrl?: string | null
}

/** 租赁记录摘要（车辆二维码扫码结果 / 车辆档案详情内嵌） */
export interface VehicleOrderBrief {
  orderId: number
  orderNo: string
  customerName?: string
  contactPhone?: string
  leaseStart: string
  leaseEnd: string
  actualReturn?: string
  orderStatus: string
  totalRent: number
  deposit: number
}

/** 扫码（二维码 / 车辆编号）查询结果：车辆档案 + 租赁记录 */
export interface VehicleQrDetail extends Vehicle {
  /** 车辆当前是否在租 */
  rented: boolean
  /** 进行中的订单，无则 null */
  currentOrder?: VehicleOrderBrief | null
  orders: VehicleOrderBrief[]
}

export interface Customer {
  customerId: number
  /** 注册用户名 */
  loginAccount?: string
  idCard?: string
  fullName: string
  contactPhone?: string
  memberLevel: string
  memberDiscount: number
  createTime: string
  /** 租车资料是否完善（后端计算字段） */
  profileCompleted?: boolean
  /** 账号状态：正常 / 已注销。注销是逻辑注销，档案与历史订单保留；注销后 loginAccount 清空显示 — */
  accountStatus?: string
  /** 乐观并发版本号：编辑提交时回传，后端比对拦截并发覆盖 */
  version?: number
  /** 历史逾期次数（有逾期费的结算单数）——信用标签，后端按结算视图现算 */
  overdueCount?: number
  /** 累计损坏赔付金额——信用标签 */
  damageTotal?: number
  /** 补缴未清的结算单数——信用标签，直接影响下次能不能免押 */
  unpaidSupplementCount?: number
}

/** 会员类型（权益表 member_rights）：「会员管理」页的唯一维护对象 */
export interface MemberRights {
  rightsId: number
  memberLevel: string
  /** 折扣系数（0.85 = 8.5 折），越小越优惠 */
  discount: number
  minRentCount?: number | null
  /** 自助购买价（元），null 表示未开放购买 */
  purchasePrice?: number | null
  rightsDesc?: string | null
  /** 挂靠该等级的客户数 */
  memberCount: number
}

export interface Order {
  orderId: number
  orderNo: string
  customerName: string
  vehicleNo: string
  /** 车辆品牌：订单车辆列以「车牌号(品牌名)」展示 */
  vehicleBrand?: string | null
  leaseStart: string
  leaseEnd: string
  totalRent: number
  /** 优惠抵扣合计（券 + 积分），只抵租金 */
  discountAmount?: number
  /** 增值服务费（已含在 totalRent 内） */
  addonFee?: number
  /** 本单抵扣的积分数 */
  pointsUsed?: number
  /** 本单使用的顾客券（customer_coupon.id） */
  customerCouponId?: number | null
  /** 下单时固化的会员折扣率 */
  rentDiscount?: number
  /** 结算后的实际租金；未结算为 null（下单时的 totalRent 只是预收预估） */
  actualRent?: number | null
  deposit: number
  orderStatus: string
  createTime: string
  /** 下单时选定的支付方式（落库在 payment_record.pay_type），订单详情据此展示 */
  payType?: string | null
  /** 提前还车申请状态：null = 未申请；「待审核」= 顾客已申请、等前台审核 */
  earlyReturnStatus?: string | null
  /** 租期是否已到期（到期订单在归还结算页置顶高亮） */
  isOverdue?: boolean
}

export interface StaffUser {
  userId: number
  loginAccount: string
  userName: string
  roleName: string
  userStatus: number
  loginFailCount: number
  mustChangePassword?: boolean
}


export interface RenewalRecord {
  renewalId: number
  orderId: number
  renewalDays: number
  renewalFee: number
  oldEndTime: string
  newEndTime: string
  renewalTime: string
}

export interface CarCheckList {
  checkId: number
  checkNo: string
  checkDate: string
  vehicleId: number
  /** 车牌（后端投影 DTO 直接给，不再嵌套整个车辆对象） */
  vehicleNo?: string
  /** 兼容旧返回：早期接口内嵌整个车辆对象 */
  vehicle?: Vehicle
  systemStatus: string
  actualStatus: string
  isMatch: boolean
  differenceDesc?: string
}

export interface CheckReport {
  reportId: number
  reportNo: string
  checkDate: string
  totalCount: number
  matchCount: number
  diffCount: number
  reportContent?: string
  generateTime: string
}

/** 预警/通知条目 */
export interface NotificationItem {
  /** 落库后的消息主键（通用 message 表），已读/删除按此定位 */
  messageId?: number
  id: string
  level: 'danger' | 'warning' | 'info' | 'success'
  category: string
  title: string
  description: string
  time: string
  link?: string
  /** 当前登录用户是否已读（后端按通用 message 表的消息行逐条返回） */
  isRead?: boolean
  /** 已读时间（未读为空） */
  readTime?: string
}

/** 顾客消息中心条目（通用 message 表，receiver_type='customer'）
 *
 * 字段名与后端 JSON 严格一致（ASP.NET Core 默认 camelCase 序列化）。
 * 之前这里写成了 MessageId / Title 这类 PascalCase，运行时全部取到 undefined，
 * 表现为「点消息不变已读、单条删除无效」，所以必须用类型把字段名钉死。
 */
export interface CustomerMessage {
  messageId: number
  /** 业务来源：refund / supplement / supplement_paid 等，决定左侧图标 */
  refType?: string | null
  refId?: string | null
  title: string
  content: string
  level?: string | null
  /** 点击后跳转的前端路由（可为空） */
  link?: string | null
  isRead: boolean
  createdAt: string
  /** 已读时间（未读为空） */
  readAt?: string | null
}

/** 顾客名下的线上补缴单（门店结算选「线上补缴」时生成的 payment_record，biz_type='supplement'） */
export interface SupplementPayItem {
  payId: number
  orderId: number
  orderNo: string
  /** 应补缴金额 */
  payAmount: number
  payType?: string | null
  /** 待支付 / 已支付 / 已退款 … */
  payStatus: string
  payTime?: string | null
  transactionNo?: string | null
}

/** 顾客端可租车辆（窄字段，不含经营数据） */
export interface RentableVehicle {
  vehicleId: number
  vehicleNo: string
  brand?: string
  vehicleStatus: string
  /** 该车日租金（已解析：未单独定价时为系统默认价） */
  dailyRent: number
  /** 该车小时租金 = 日租金 ÷ 24 */
  hourlyRent: number
  /** 该车押金（已解析） */
  deposit: number
  /** 是否单独定价（false = 用的系统默认价） */
  customPriced: boolean
  /** 车辆图片相对路径；无图时前端显示默认车图 */
  imageUrl?: string | null
  /** 购入时间（顾客端「新车优先」排序依据） */
  buyTime?: string | null
  /** 近 90 天成单量（顾客端「人气优先」排序依据） */
  orderCount?: number
}

/** 线上租车费用试算结果 */
export interface RentEstimate {
  /** 计费小时数（不足 1 小时按 1 小时） */
  hours: number
  /** 兼容旧字段：值等于 hours，已不用于展示 */
  days: number
  dailyRent: number
  /** 小时租金 = 日租金 ÷ 24 */
  hourlyRent: number
  /** 应收押金（已含信用倍数） */
  deposit: number
  /** 押金基数（未乘信用倍数），用于向顾客解释为什么多收 */
  depositBase?: number
  /** 信用受限说明（押金倍数 / 租期上限的原因）；无受限时为空 */
  creditNote?: string | null
  /** 单次租期上限（天）；为空表示不受限 */
  maxLeaseDays?: number | null
  discount: number
  totalRent: number
  payable: number
}

export interface DashboardStats {
  totalRevenue: number
  totalOrders: number
  totalCustomers: number
  totalMaintenanceFee: number
  monthlyRevenue: Record<string, number>
  /** 与 monthlyRevenue 同一组日期键的订单量序列（工作台「订单量趋势」用） */
  orderTrend: Record<string, number>
  vehicleStatusStats: Record<string, number>
  orderStatusStats: Record<string, number>
  todayRevenue: number
  todayOrders: number
  rentedCount: number
  freeCount: number
  activeOrderCount: number
  overdueCount: number
  expiringCount: number
  pendingPayCount: number
  maintenanceCount: number
  incompleteProfileCount: number
  /** 保养到期车辆数（超过 maintain_cycle_days 未保养；周期为 0 时为 0） */
  maintenanceDueCount: number
}

export interface ApiResponse<T> {
  code: number
  message: string
  success: boolean
  data: T
}

/** 热销车辆卡片（顾客线上租车页推荐位） */
export interface PopularVehicle {
  vehicleId: number
  vehicleNo: string
  brand?: string
  imageUrl?: string | null
  vehicleStatus: string
  dailyRent: number
  hourlyRent: number
  deposit: number
  /** 统计区间内的成单量 */
  orderCount: number
}

/* ============================================================
   请求体类型（payload）
   ------------------------------------------------------------
   为什么前端要单独维护一份：后端的入参契约是 DTO（DTOs/*.cs），
   前端此前一律写成 `data: any`，等于把「哪些字段必填、哪些字段为空要传 null
   而不是 0」这些规则只记在注释和调用处的 if 里，写错要在联调甚至线上才暴露。
   这里逐个对齐后端 DTO 的字段名与可空性，只做类型约束、不加运行时逻辑。
   ============================================================ */

/** 新增员工账号（后端 DTOs/UserDtos.cs → CreateUserDto） */
export interface CreateUserPayload {
  loginAccount: string
  password: string
  userName: string
  /** 仅 reception（前台）/ finance（财务）可建；管理员账号不由此接口创建 */
  roleType: string
}

/** 编辑员工账号（UpdateUserDto）：password 留空表示不改密码 */
export interface UpdateUserPayload {
  userName?: string
  password?: string
  /** 1=启用 2=停用 */
  userStatus?: number
}

/** 前台代客建档（CustomerDtos.cs → CustomerCreateDto） */
export interface CreateCustomerPayload {
  /** 登录用户名，可空：线下客户没有线上账号 */
  loginAccount?: string
  idCard?: string
  fullName?: string
  contactPhone?: string
  memberLevel?: string
}

/** 编辑客户档案（CustomerUpdateDto）：version 必填，后端按它做乐观并发校验 */
export interface UpdateCustomerPayload {
  fullName?: string
  idCard?: string
  contactPhone?: string
  memberLevel?: string
  /** 打开表单时看到的版本号；冲突时后端报「刚被其他人员修改」 */
  version: number
}

/** 新增 / 编辑车辆档案（VehicleDtos.cs → VehicleSaveDto） */
export interface SaveVehiclePayload {
  vehicleNo: string
  frameNumber?: string | null
  brand?: string | null
  /** ISO 日期字符串；空 = 保留原值（历史车辆无采购日期不强求补录） */
  buyTime?: string | null
  buyPrice?: number | null
  /** 空闲 / 维保 / 报废；「在租」由开单流程自动产生，不接受手工设置 */
  vehicleStatus: string
  /** 留空必须传 null（不是 0）：0 会被理解成「本车日租金 0 元」 */
  dailyRent?: number | null
  deposit?: number | null
  imageUrl?: string | null
}

/** 批量编辑车辆（BatchVehicleUpdateDto）：每个字段一个 Apply 开关，只覆盖勾选项 */
export interface BatchVehicleUpdatePayload {
  ids: number[]
  applyStatus: boolean
  vehicleStatus?: string | null
  applyDailyRent: boolean
  dailyRent?: number | null
  applyDeposit: boolean
  deposit?: number | null
  applyBrand: boolean
  brand?: string | null
}

/** 柜台线下开单（OrderDtos.cs → CreateOrderDto） */
export interface CreateOrderPayload {
  /** 0 = 线下新客户，改由 newCustomer* 传入资料，后端自动建档 */
  customerId: number
  vehicleId: number
  /** datetime-local 的本地时间字符串，**不能**用 toISOString()（会整体差 8 小时） */
  leaseStart: string
  leaseEnd: string
  payType?: string
  /** 租车环节补录的实名资料（客户资料未完善时必填） */
  idCard?: string
  contactPhone?: string
  fullName?: string
  newCustomerName?: string
  newCustomerIdCard?: string
  newCustomerPhone?: string
  /** 本单使用的顾客券（customer_coupon.id），不传表示不用券 */
  customerCouponId?: number | null
  /** 本单抵扣的积分数，0 表示不抵扣 */
  pointsUsed?: number
  /** 勾选的增值服务（addon_item.addon_id） */
  addonIds?: number[]
  /** 客户端幂等键：双击/网络重试时后端返回首次创建的订单，不会重复扣券或占车 */
  requestId?: string
}

/** 归还结算（OrderDtos.cs → SettleOrderDto）：逾期费由后端按实际还车时间算，前端不传 */
export interface SettleOrderPayload {
  orderId: number
  damageFee: number
  remark?: string | null
  /** 线下 / 线上（仅本次结算产生补缴金额时有意义） */
  supplementPayMethod?: string
}

/* ============================================================
   运营与顾客权益（券 / 增值服务 / 积分 / 交接班 / 订单变更 / 合同）
   ------------------------------------------------------------
   字段与 back/DTOs/OperationsDtos.cs 一一对应。
   ============================================================ */

/** 优惠券模板（运营配置页） */
export interface Coupon {
  couponId: number
  name: string
  amount: number
  /** 使用门槛：订单租金达到该金额才可用，0 表示无门槛 */
  minAmount: number
  validFrom: string
  validTo: string
  /** 发放总量，0 表示不限量 */
  totalQty: number
  issuedQty: number
  /** 同一顾客最多可领张数 */
  perLimit: number
  status: string
  createTime: string
  /** 兑换码（口令领券）；未设置为空 */
  redeemCode?: string | null
  /** 是否开放顾客自助兑换 */
  redeemOpen: boolean
}

export interface CouponInput {
  name: string
  amount: number
  minAmount?: number
  validFrom: string
  validTo: string
  /** 0 = 不限量 */
  totalQty?: number
  perLimit?: number
  /** 兑换码：4~20 位字母数字，留空表示不开放口令领取 */
  redeemCode?: string | null
  /** 是否开放顾客自助兑换 */
  redeemOpen?: boolean
}

export interface GrantCouponPayload {
  couponId: number
  /** 为空表示发给全部正常状态顾客 */
  customerIds?: number[]
}

/** 顾客持有的券（我的券 / 下单选券） */
export interface MyCoupon {
  id: number
  couponId: number
  name: string
  amount: number
  minAmount: number
  expireTime: string
  /** 未使用 / 已使用 / 已过期 */
  status: string
  receiveTime: string
  orderId?: number | null
  /** 单张券唯一码值：顾客端据此生成核销二维码 */
  code?: string | null
}

/** 前台扫码查券结果（券码天然绑定持券顾客） */
export interface CouponVerifyInfo {
  customerCouponId: number
  couponId: number
  code: string
  name: string
  amount: number
  minAmount: number
  status: string
  expireTime: string
  useTime?: string | null
  orderId?: number | null
  customerId: number
  customerName: string
  contactPhone?: string | null
  idCardMasked?: string | null
  memberLevel: string
  usable: boolean
}

/** 增值服务项 */
export interface AddonItem {
  addonId: number
  name: string
  price: number
  /** 一次性 / 按天 */
  unit: string
  description?: string | null
  status: string
  sortNo: number
}

export interface AddonInput {
  name: string
  price: number
  unit: string
  description?: string | null
  sortNo?: number
}

/** 订单增值服务明细快照 */
export interface OrderAddon {
  name: string
  unit: string
  price: number
  qty: number
  amount: number
}

/** 积分概要 */
export interface PointSummary {
  balance: number
  /** 本次最多可抵扣的积分数（按租金 10% 封顶） */
  maxRedeem: number
  maxRedeemAmount: number
  rule: string
  logs: PointLog[]
}

export interface PointLog {
  changeAmount: number
  balance: number
  /** 结算奖励 / 下单抵扣 / 取消退回 / 人工调整 */
  source: string
  remark?: string | null
  orderId?: number | null
  createTime: string
}

export interface PointRedeemPreview {
  points: number
  amount: number
  maxPoints: number
  maxAmount: number
}

/** 交接班 */
export interface Shift {
  shiftId: number
  shiftNo: string
  operatorName: string
  openTime: string
  closeTime?: string | null
  openingCash: number
  /** 本班现金净收入（收款 − 退款） */
  cashCollected: number
  cashActual: number
  /** 实点 −（备用金 + 现金净收入），正数长款、负数短款 */
  difference: number
  alipayAmount: number
  wechatAmount: number
  bankAmount: number
  orderCount: number
  settleCount: number
  status: string
  note?: string | null
  /** 进行中的班次：系统实时汇总 */
  isCurrent: boolean
}

export interface OpenShiftPayload {
  openingCash: number
}

export interface CloseShiftPayload {
  shiftId: number
  cashActual: number
  note?: string | null
}

/** 订单变更（换车 / 改期） */
export interface OrderChange {
  changeId: number
  orderId: number
  /** 换车 / 改期 */
  changeType: string
  oldVehicleId?: number | null
  newVehicleId?: number | null
  oldStart?: string | null
  oldEnd?: string | null
  newStart?: string | null
  newEnd?: string | null
  oldRent: number
  newRent: number
  /** 正数补缴、负数退款 */
  diffAmount: number
  reason?: string | null
  createTime: string
}

export interface ChangeVehiclePayload {
  orderId: number
  newVehicleId: number
  reason?: string | null
}

export interface ReschedulePayload {
  orderId: number
  leaseStart: string
  leaseEnd: string
  reason?: string | null
}

/** 车辆档案时间线 */
export interface VehicleArchive {
  vehicleId: number
  vehicleNo: string
  brand?: string | null
  vehicleStatus: string
  imageUrl?: string | null
  frameNumber?: string | null
  buyTime?: string | null
  buyPrice?: number | null
  dailyRent?: number | null
  deposit?: number | null
  totalRevenue: number
  maintenanceCost: number
  orderCount: number
  events: VehicleArchiveEvent[]
}

export interface VehicleArchiveEvent {
  time: string
  /** 租赁 / 维保 / 盘点 / 报废 / 验车 */
  type: string
  title: string
  detail?: string | null
  amount?: number | null
  status?: string | null
}

/** 电子合同 */
export interface Contract {
  contractId: number
  contractNo: string
  orderId: number
  version: string
  contentHash: string
  signatureUrl?: string | null
  signTime: string
  status: string
  content: string
}

export interface SignContractPayload {
  orderId: number
  /** 手写签名 PNG 的 data URL */
  signatureData: string
}
