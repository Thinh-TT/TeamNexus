import {
  CheckCircleOutlined,
  CloseCircleOutlined,
  ExclamationCircleOutlined,
  LoadingOutlined,
  QuestionCircleOutlined,
} from '@ant-design/icons'
import { Tag } from 'antd'
import type { AgentRunStatus, AgentStopReason } from '../types/agentRun.types'

export const formatBytes = (bytes: number): string => {
  if (bytes <= 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.min(Math.floor(Math.log(bytes) / Math.log(k)), sizes.length - 1)
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`
}

export const getStatusTag = (status: AgentRunStatus) => {
  switch (status) {
    case 'Running':
      return (
        <Tag color="processing" icon={<LoadingOutlined spin />} style={{ fontWeight: 600 }}>
          Đang chạy
        </Tag>
      )
    case 'AwaitingClarification':
      return (
        <Tag color="warning" icon={<QuestionCircleOutlined />} style={{ fontWeight: 600 }}>
          Chờ làm rõ
        </Tag>
      )
    case 'AwaitingApproval':
      return (
        <Tag color="gold" icon={<ExclamationCircleOutlined />} style={{ fontWeight: 600 }}>
          Chờ duyệt
        </Tag>
      )
    case 'Completed':
      return (
        <Tag color="success" icon={<CheckCircleOutlined />} style={{ fontWeight: 600 }}>
          Hoàn thành
        </Tag>
      )
    case 'Failed':
      return (
        <Tag color="error" icon={<CloseCircleOutlined />} style={{ fontWeight: 600 }}>
          Thất bại
        </Tag>
      )
    default:
      return <Tag>{status}</Tag>
  }
}

export const getStopReasonMessage = (
  reason: AgentStopReason | null,
  error?: string | null
): string => {
  if (error) return error

  switch (reason) {
    case 'ToolLimit':
      return 'Đã đạt giới hạn số lần gọi công cụ (15 tool calls).'
    case 'TimeLimit':
      return 'Đã quá thời gian thực thi tối đa (5 phút).'
    case 'TokenBudget':
      return 'Đã vượt quá hạn mức token cho phép (50.000 tokens).'
    case 'ProviderError':
      return 'Lỗi kết nối hoặc xử lý từ phía nhà cung cấp AI.'
    case 'Cancelled':
      return 'Tác vụ đã bị người dùng huỷ bỏ.'
    case 'TaskChanged':
      return 'Task đã bị thay đổi hoặc chuyển đổi trong khi Agent đang chạy.'
    case 'InternalError':
      return 'Lỗi nội bộ hệ thống trong quá trình thực thi.'
    case 'DraftProduced':
      return 'Đã tạo xong bản thảo kết quả và đang chờ phê duyệt.'
    case 'QuestionAsked':
      return 'Agent đã yêu cầu làm rõ thêm thông tin.'
    default:
      return 'Tác vụ kết thúc.'
  }
}
