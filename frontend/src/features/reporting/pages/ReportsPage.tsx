import React, { useState } from 'react'
import {
  ArrowLeftOutlined,
  DownloadOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Avatar,
  Button,
  Card,
  Flex,
  Layout,
  Result,
  Space,
  Spin,
  Tooltip,
  Typography,
} from 'antd'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useAuth } from '../../auth/hooks/useAuth'
import { useWorkspaceRole } from '../../../shared/hooks/useWorkspaceRole'
import { ReportExportDrawer } from '../components/ReportExportDrawer'
import { ReportFilters } from '../components/ReportFilters'
import { ReportSummaryPanel } from '../components/ReportSummaryPanel'
import { useReportSummary } from '../hooks/useReportSummary'

const { Header, Content } = Layout

export const ReportsPage: React.FC = () => {
  const { workspaceId = '' } = useParams<{ workspaceId: string }>()
  const [searchParams, setSearchParams] = useSearchParams()
  const navigate = useNavigate()
  const { user, logout } = useAuth()

  const initialBoardId = searchParams.get('boardId') || undefined
  const [selectedBoardId, setSelectedBoardId] = useState<string | undefined>(initialBoardId)
  const [from, setFrom] = useState<string | undefined>(undefined)
  const [to, setTo] = useState<string | undefined>(undefined)

  const { isManagerOrAdmin, loading: checkingRole } = useWorkspaceRole(workspaceId)
  const [exportDrawerOpen, setExportDrawerOpen] = useState<boolean>(false)

  const { report, status, error, reload, setBoard, setRange } = useReportSummary(
    isManagerOrAdmin ? workspaceId : '',
    { boardId: selectedBoardId, from, to }
  )

  const handleFilterChange = (params: { boardId?: string; from?: string; to?: string }) => {
    setSelectedBoardId(params.boardId)
    setFrom(params.from)
    setTo(params.to)

    // Update query params in URL
    const newParams = new URLSearchParams()
    if (params.boardId) newParams.set('boardId', params.boardId)
    setSearchParams(newParams, { replace: true })

    setBoard(params.boardId)
    setRange(params.from, params.to)
  }

  if (checkingRole) {
    return (
      <Flex align="center" justify="center" style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
        <Spin size="large" description="Đang kiểm tra quyền truy cập..." />
      </Flex>
    )
  }

  if (!isManagerOrAdmin) {
    return (
      <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
        <Header
          style={{
            backgroundColor: '#0f172a',
            padding: '0 24px',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'space-between',
          }}
        >
          <Typography.Title level={4} style={{ color: '#fff', margin: 0 }}>
            TeamNexus
          </Typography.Title>
          <Space>
            <Button type="primary" danger onClick={() => logout()}>
              Đăng xuất
            </Button>
          </Space>
        </Header>
        <Content style={{ padding: '60px 24px', maxWidth: 800, margin: '0 auto', width: '100%' }}>
          <Card style={{ borderRadius: 12 }}>
            <Result
              status="403"
              title="403 - Quyền Truy Cập Bị Từ Chối"
              subTitle="Chỉ Quản lý (Manager) hoặc Quản trị viên (Admin) của workspace mới có quyền xem và xuất báo cáo dữ liệu."
              extra={
                <Button type="primary" onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}>
                  Quay lại danh sách bảng
                </Button>
              }
            />
          </Card>
        </Content>
      </Layout>
    )
  }

  return (
    <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
      {/* App Header */}
      <Header
        style={{
          backgroundColor: '#0f172a',
          padding: '0 24px',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}
      >
        <Flex align="center" gap={12}>
          <Typography.Title level={4} style={{ color: '#fff', margin: 0 }}>
            TeamNexus
          </Typography.Title>
        </Flex>

        <Space size="middle">
          <Avatar src={user?.avatarUrl} style={{ backgroundColor: '#6366f1' }}>
            {user?.displayName?.[0]?.toUpperCase() ?? 'U'}
          </Avatar>
          <Typography.Text style={{ color: '#fff', fontWeight: 500 }}>
            {user?.displayName ?? user?.email}
          </Typography.Text>
          <Button type="primary" danger onClick={() => logout()}>
            Đăng xuất
          </Button>
        </Space>
      </Header>

      {/* Main Content */}
      <Content style={{ padding: '32px 24px', maxWidth: 1200, margin: '0 auto', width: '100%' }}>
        <Flex vertical gap="large">
          {/* Top Controls Bar */}
          <Flex justify="space-between" align="center" wrap="wrap" gap={12}>
            <Flex align="center" gap={12}>
              <Button
                type="text"
                icon={<ArrowLeftOutlined />}
                onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}
              />
              <div>
                <Typography.Title level={3} style={{ margin: 0, color: '#0f172a' }}>
                  Báo Cáo & Xuất Dữ Liệu
                </Typography.Title>
                <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                  {report ? `${report.workspaceName} ${report.scope.boardName ? `› ${report.scope.boardName}` : ''}` : `Workspace: ${workspaceId}`}
                </Typography.Text>
              </div>
            </Flex>

            <Space wrap>
              <Tooltip title="Tải lại dữ liệu">
                <Button icon={<ReloadOutlined />} onClick={() => reload()} loading={status === 'loading'} />
              </Tooltip>
              <Button
                type="primary"
                icon={<DownloadOutlined />}
                style={{ backgroundColor: '#6366f1', borderRadius: 8 }}
                onClick={() => setExportDrawerOpen(true)}
              >
                Xuất Báo Cáo
              </Button>
            </Space>
          </Flex>

          {/* Filters Card */}
          <Card
            styles={{ body: { padding: '16px 20px' } }}
            style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
          >
            <ReportFilters
              workspaceId={workspaceId}
              selectedBoardId={selectedBoardId}
              from={from}
              to={to}
              onChange={handleFilterChange}
              loading={status === 'loading'}
            />
          </Card>

          {/* Content Body */}
          {error ? (
            <Alert
              type="error"
              showIcon
              message="Không thể tải báo cáo"
              description={error}
              action={
                <Button size="small" type="primary" onClick={() => reload()}>
                  Thử lại
                </Button>
              }
            />
          ) : status === 'loading' && !report ? (
            <Flex align="center" justify="center" style={{ minHeight: 300 }}>
              <Spin size="large" description="Đang tổng hợp báo cáo dữ liệu..." />
            </Flex>
          ) : report ? (
            <ReportSummaryPanel report={report} />
          ) : null}
        </Flex>

        {/* Export Drawer */}
        <ReportExportDrawer
          open={exportDrawerOpen}
          onClose={() => setExportDrawerOpen(false)}
          workspaceId={workspaceId}
          boardId={selectedBoardId}
          from={from}
          to={to}
        />
      </Content>
    </Layout>
  )
}
