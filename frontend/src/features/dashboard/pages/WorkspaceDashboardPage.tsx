import React from 'react'
import {
  ArrowLeftOutlined,
  CheckCircleOutlined,
  ClockCircleOutlined,
  ExclamationCircleOutlined,
  ProjectOutlined,
  ReloadOutlined,
  TeamOutlined,
  UserOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Col,
  Flex,
  Layout,
  Result,
  Row,
  Space,
  Spin,
  Statistic,
  Tooltip,
  Typography,
} from 'antd'
import { useNavigate, useParams } from 'react-router-dom'
import { AppHeader } from '../../../shared/components/AppHeader'
import { useDashboard } from '../hooks/useDashboard'
import { MyTasksPanel } from '../components/MyTasksPanel'
import { ObserverAlertsPanel } from '../components/ObserverAlertsPanel'
import { BoardSummaryPanel } from '../components/BoardSummaryPanel'
import { RecentActivityPanel } from '../components/RecentActivityPanel'
import { ProjectHealthGauge } from '../components/ProjectHealthGauge'

const { Content } = Layout

export const WorkspaceDashboardPage: React.FC = () => {
  const { workspaceId } = useParams<{ workspaceId: string }>()
  const navigate = useNavigate()

  const { dashboard, status, error, httpStatus, reload } = useDashboard(workspaceId ?? '')

  if (!workspaceId) {
    return (
      <Layout style={{ minHeight: '100vh', background: '#f8fafc' }}>
        <AppHeader />
        <Content style={{ padding: '48px 24px', maxWidth: 800, margin: '0 auto', width: '100%' }}>
          <Result
            status="error"
            title="Lỗi đường dẫn"
            subTitle="Mã workspace không hợp lệ hoặc thiếu trong đường dẫn URL"
            extra={
              <Button type="primary" onClick={() => navigate('/')}>
                Về trang chủ
              </Button>
            }
          />
        </Content>
      </Layout>
    )
  }

  return (
    <Layout style={{ minHeight: '100vh', background: '#f8fafc' }}>
      <AppHeader workspaceId={workspaceId}>
        <Button
          icon={<ProjectOutlined />}
          style={{ borderRadius: 8, borderColor: '#cbd5e1', color: '#475569', background: '#f8fafc' }}
          onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}
          data-testid="nav-boards-header-btn"
        >
          Bảng Kanban
        </Button>
        <Button
          icon={<TeamOutlined />}
          style={{ borderRadius: 8, borderColor: '#cbd5e1', color: '#475569', background: '#f8fafc' }}
          onClick={() => navigate(`/workspaces/${workspaceId}/members`)}
          data-testid="nav-members-header-btn"
        >
          Thành viên
        </Button>
      </AppHeader>

      <Content style={{ padding: '32px 24px', maxWidth: 1200, margin: '0 auto', width: '100%' }}>
        {status === 'loading' ? (
          <Flex align="center" justify="center" style={{ height: 350 }}>
            <Spin size="large" tip="Đang tải dữ liệu tổng quan..." />
          </Flex>
        ) : status === 'error' ? (
          <Result
            status={httpStatus === 404 ? '404' : 'error'}
            title={httpStatus === 404 ? '404' : 'Lỗi tải dữ liệu'}
            subTitle={
              httpStatus === 404
                ? 'Không tìm thấy workspace hoặc bạn không phải là thành viên.'
                : error || 'Không thể tải thông tin tổng quan workspace.'
            }
            extra={
              <Space>
                <Button type="primary" onClick={() => navigate('/')}>
                  Về trang chủ
                </Button>
                {httpStatus !== 404 && (
                  <Button icon={<ReloadOutlined />} onClick={reload}>
                    Thử lại
                  </Button>
                )}
              </Space>
            }
          />
        ) : dashboard ? (
          <Flex vertical gap="large">
            {/* Header controls */}
            <Flex justify="space-between" align="center" wrap="wrap" gap={12}>
              <Flex align="center" gap={8}>
                <Button
                  type="text"
                  icon={<ArrowLeftOutlined />}
                  onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}
                />
                <div>
                  <Typography.Title level={3} style={{ margin: 0, color: '#0f172a' }}>
                    Tổng Quan Workspace: {dashboard.workspaceName}
                  </Typography.Title>
                  <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                    Cập nhật theo thời gian thực trên toàn bộ workspace
                  </Typography.Text>
                </div>
              </Flex>

              <Space>
                <Tooltip title="Làm mới">
                  <Button icon={<ReloadOutlined />} onClick={reload} data-testid="dashboard-reload-btn">
                    Làm mới
                  </Button>
                </Tooltip>
                <Button
                  type="primary"
                  icon={<ProjectOutlined />}
                  style={{ backgroundColor: '#6366f1', borderRadius: 8 }}
                  onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}
                >
                  Xem Bảng Kanban
                </Button>
              </Space>
            </Flex>

            {/* Quick Summary KPI Cards & Project Health */}
            <Row gutter={[16, 16]} align="stretch">
              <Col xs={12} sm={8} lg={4}>
                <Card style={{ borderRadius: 10, border: '1px solid #e2e8f0', height: '100%' }}>
                  <Statistic
                    title="Tổng số thẻ"
                    value={dashboard.summary.totalTasks}
                    prefix={<ProjectOutlined style={{ color: '#6366f1' }} />}
                  />
                </Card>
              </Col>
              <Col xs={12} sm={8} lg={4}>
                <Card style={{ borderRadius: 10, border: '1px solid #e2e8f0', height: '100%' }}>
                  <Statistic
                    title="Thẻ đã hoàn thành"
                    value={dashboard.summary.doneTasks}
                    prefix={<CheckCircleOutlined style={{ color: '#10b981' }} />}
                    valueStyle={{ color: '#10b981' }}
                  />
                </Card>
              </Col>
              <Col xs={12} sm={8} lg={4}>
                <Card style={{ borderRadius: 10, border: '1px solid #e2e8f0', height: '100%' }}>
                  <Statistic
                    title="Thẻ đang mở"
                    value={dashboard.summary.openTasks}
                    prefix={<ClockCircleOutlined style={{ color: '#3b82f6' }} />}
                    valueStyle={{ color: '#3b82f6' }}
                  />
                </Card>
              </Col>
              <Col xs={12} sm={8} lg={4}>
                <Card style={{ borderRadius: 10, border: '1px solid #e2e8f0', height: '100%' }}>
                  <Statistic
                    title="Thẻ quá hạn"
                    value={dashboard.summary.overdueTasks}
                    prefix={<ExclamationCircleOutlined style={{ color: '#ef4444' }} />}
                    valueStyle={{ color: dashboard.summary.overdueTasks > 0 ? '#ef4444' : undefined }}
                  />
                </Card>
              </Col>
              <Col xs={12} sm={8} lg={4}>
                <Card style={{ borderRadius: 10, border: '1px solid #e2e8f0', height: '100%' }}>
                  <Statistic
                    title="Thẻ mở của tôi"
                    value={dashboard.summary.myOpenTasks}
                    prefix={<UserOutlined style={{ color: '#8b5cf6' }} />}
                    valueStyle={{ color: '#8b5cf6' }}
                  />
                </Card>
              </Col>
              <Col xs={24} sm={8} lg={4}>
                <ProjectHealthGauge health={dashboard.health} />
              </Col>
            </Row>

            {/* Upper Row: My Tasks (left) + Observer Alerts (right) */}
            <Row gutter={[16, 16]}>
              <Col xs={24} lg={15}>
                <MyTasksPanel workspaceId={workspaceId} myTasks={dashboard.myTasks} />
              </Col>
              <Col xs={24} lg={9}>
                <ObserverAlertsPanel workspaceId={workspaceId} />
              </Col>
            </Row>

            {/* Lower Row: Board Summary (left) + Recent Activity (right) */}
            <Row gutter={[16, 16]}>
              <Col xs={24} lg={14}>
                <BoardSummaryPanel
                  workspaceId={workspaceId}
                  boards={dashboard.boards}
                  boardsTruncated={dashboard.boardsTruncated}
                />
              </Col>
              <Col xs={24} lg={10}>
                <RecentActivityPanel
                  workspaceId={workspaceId}
                  activities={dashboard.recentActivities}
                />
              </Col>
            </Row>
          </Flex>
        ) : null}
      </Content>
    </Layout>
  )
}
