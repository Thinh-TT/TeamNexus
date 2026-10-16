import React from 'react'
import {
  ArrowLeftOutlined,
  CheckCircleOutlined,
  ClockCircleOutlined,
  ExclamationCircleOutlined,
  ProjectOutlined,
  ReloadOutlined,
  ThunderboltFilled,
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
  Tag,
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
      <AppHeader workspaceId={workspaceId} />

      <Content style={{ padding: '28px 24px', maxWidth: 1360, margin: '0 auto', width: '100%' }}>
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
            {/* Header controls — Luminous Executive Command Banner */}
            <div
              style={{
                padding: '18px 20px',
                borderRadius: 12,
                backgroundColor: '#ffffff',
                border: '1px solid #e2e8f0',
                boxShadow: '0 1px 2px 0 rgba(15, 23, 42, 0.03)',
              }}
            >
              <Flex justify="space-between" align="center" wrap="wrap" gap={12}>
                <Flex align="center" gap={10}>
                  <Button
                    type="text"
                    icon={<ArrowLeftOutlined />}
                    onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}
                    style={{ borderRadius: 8 }}
                  />
                  <div>
                    <Flex align="center" gap={8} wrap="wrap">
                      <Typography.Title level={3} style={{ margin: 0, color: '#0f172a', fontWeight: 700, letterSpacing: '-0.02em' }}>
                        Tổng Quan Workspace: {dashboard.workspaceName}
                      </Typography.Title>
                      <Tag
                        color="success"
                        style={{ margin: 0, borderRadius: 9999, fontWeight: 600, fontSize: 11, padding: '0 8px' }}
                      >
                        SignalR Live
                      </Tag>
                      <Tag
                        color="purple"
                        style={{ margin: 0, borderRadius: 9999, fontWeight: 600, fontSize: 11, padding: '0 8px' }}
                      >
                        <ThunderboltFilled style={{ fontSize: 10, marginRight: 4 }} />
                        Jev Sys-1 + DeepSeek Sys-2
                      </Tag>
                    </Flex>
                    <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                      Cập nhật theo thời gian thực trên toàn bộ workspace
                    </Typography.Text>
                  </div>
                </Flex>

                <Space>
                  <Tooltip title="Làm mới">
                    <Button
                      icon={<ReloadOutlined />}
                      onClick={reload}
                      data-testid="dashboard-reload-btn"
                      style={{ borderRadius: 8 }}
                    >
                      Làm mới
                    </Button>
                  </Tooltip>
                  <Button
                    type="primary"
                    icon={<ProjectOutlined />}
                    style={{
                      backgroundColor: '#4f46e5',
                      borderColor: '#4f46e5',
                      borderRadius: 8,
                      fontWeight: 600,
                    }}
                    onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}
                  >
                    Xem Bảng Kanban
                  </Button>
                </Space>
              </Flex>
            </div>

            {/* Quick Summary KPI Cards & Project Health (Bento Row) */}
            <Row gutter={[16, 16]} align="stretch">
              <Col xs={12} sm={8} lg={4}>
                <Card
                  style={{
                    borderRadius: 12,
                    border: '1px solid #e2e8f0',
                    borderTop: '3px solid #4f46e5',
                    height: '100%',
                  }}
                >
                  <Statistic
                    title={<span style={{ fontWeight: 600, color: '#475569', fontSize: 12 }}>Tổng số thẻ</span>}
                    value={dashboard.summary.totalTasks}
                    prefix={<ProjectOutlined style={{ color: '#4f46e5' }} />}
                    valueStyle={{ color: '#0f172a', fontWeight: 700 }}
                  />
                </Card>
              </Col>
              <Col xs={12} sm={8} lg={4}>
                <Card
                  style={{
                    borderRadius: 12,
                    border: '1px solid #e2e8f0',
                    borderTop: '3px solid #10b981',
                    height: '100%',
                  }}
                >
                  <Statistic
                    title={<span style={{ fontWeight: 600, color: '#475569', fontSize: 12 }}>Thẻ đã hoàn thành</span>}
                    value={dashboard.summary.doneTasks}
                    prefix={<CheckCircleOutlined style={{ color: '#10b981' }} />}
                    valueStyle={{ color: '#10b981', fontWeight: 700 }}
                  />
                </Card>
              </Col>
              <Col xs={12} sm={8} lg={4}>
                <Card
                  style={{
                    borderRadius: 12,
                    border: '1px solid #e2e8f0',
                    borderTop: '3px solid #3b82f6',
                    height: '100%',
                  }}
                >
                  <Statistic
                    title={<span style={{ fontWeight: 600, color: '#475569', fontSize: 12 }}>Thẻ đang mở</span>}
                    value={dashboard.summary.openTasks}
                    prefix={<ClockCircleOutlined style={{ color: '#3b82f6' }} />}
                    valueStyle={{ color: '#3b82f6', fontWeight: 700 }}
                  />
                </Card>
              </Col>
              <Col xs={12} sm={8} lg={4}>
                <Card
                  style={{
                    borderRadius: 12,
                    border: dashboard.summary.overdueTasks > 0 ? '1px solid #fecaca' : '1px solid #e2e8f0',
                    borderTop: '3px solid #ef4444',
                    backgroundColor: dashboard.summary.overdueTasks > 0 ? '#fffafa' : '#ffffff',
                    height: '100%',
                  }}
                >
                  <Statistic
                    title={<span style={{ fontWeight: 600, color: '#475569', fontSize: 12 }}>Thẻ quá hạn</span>}
                    value={dashboard.summary.overdueTasks}
                    prefix={<ExclamationCircleOutlined style={{ color: '#ef4444' }} />}
                    valueStyle={{
                      color: dashboard.summary.overdueTasks > 0 ? '#ef4444' : '#0f172a',
                      fontWeight: 700,
                    }}
                  />
                </Card>
              </Col>
              <Col xs={12} sm={8} lg={4}>
                <Card
                  style={{
                    borderRadius: 12,
                    border: '1px solid #e2e8f0',
                    borderTop: '3px solid #7c3aed',
                    height: '100%',
                  }}
                >
                  <Statistic
                    title={<span style={{ fontWeight: 600, color: '#475569', fontSize: 12 }}>Thẻ mở của tôi</span>}
                    value={dashboard.summary.myOpenTasks}
                    prefix={<UserOutlined style={{ color: '#7c3aed' }} />}
                    valueStyle={{ color: '#7c3aed', fontWeight: 700 }}
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
