import { useEffect, useState } from 'react'
import { Button, Flex, Space, Tag, Typography } from 'antd'
import { httpClient } from './shared/api'

type HealthResponse = {
  status: string
  service: string
  time: string
}

/**
 * TeamNexus – placeholder home page (Phase 1 §1).
 * It calls GET /api/health through the Vite dev proxy to prove the full
 * frontend → proxy → backend chain works. Real screens (login, dashboard,
 * boards) arrive in later phases.
 */
function App() {
  const [backend, setBackend] = useState<'checking' | 'ok' | 'error'>('checking')

  useEffect(() => {
    let cancelled = false
    httpClient
      .get<HealthResponse>('/health')
      .then(() => {
        if (!cancelled) setBackend('ok')
      })
      .catch(() => {
        if (!cancelled) setBackend('error')
      })
    return () => {
      cancelled = true
    }
  }, [])

  const backendTag =
    backend === 'ok' ? (
      <Tag color="success">Backend: connected</Tag>
    ) : backend === 'error' ? (
      <Tag color="error">Backend: unreachable</Tag>
    ) : (
      <Tag>Backend: checking…</Tag>
    )

  return (
    <Flex vertical align="center" justify="center" gap="middle" style={{ minHeight: '100vh' }}>
      <Typography.Title level={1} style={{ marginBottom: 0 }}>
        TeamNexus
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ fontSize: 18 }}>
        Trợ lý điều phối không gian làm việc thông minh
      </Typography.Paragraph>
      <Space>{backendTag}</Space>
      <Typography.Paragraph type="secondary">
        Khởi tạo dự án – Giai đoạn 1 §1. Đăng nhập (Google/GitHub) sẽ có ở bước tiếp theo.
      </Typography.Paragraph>
      <Button type="primary" disabled>
        Bắt đầu
      </Button>
    </Flex>
  )
}

export default App
