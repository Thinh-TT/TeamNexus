import React, { useState } from 'react'
import {
  ApartmentOutlined,
  AppstoreOutlined,
  ArrowRightOutlined,
  CheckCircleFilled,
  ClockCircleOutlined,
  CodeOutlined,
  DashboardOutlined,
  FireOutlined,
  HistoryOutlined,
  LockOutlined,
  RobotOutlined,
  SafetyCertificateOutlined,
  ThunderboltOutlined,
  UsergroupAddOutlined,
} from '@ant-design/icons'
import { Badge, Button, Card, Col, Flex, Layout, Row, Space, Tag, Typography } from 'antd'
import { useAuth } from '../../auth/hooks/useAuth'
import { NexusLogo } from '../../../shared/components/NexusLogo'

const { Header, Content, Footer } = Layout
const { Title, Paragraph, Text } = Typography

const GoogleIcon: React.FC = () => (
  <svg width="18" height="18" viewBox="0 0 24 24" style={{ marginRight: 8, flexShrink: 0 }}>
    <path
      fill="#4285F4"
      d="M23.745 12.27c0-.7-.06-1.4-.19-2.07H12v4.51h6.6c-.29 1.52-1.14 2.82-2.4 3.68v3.05h3.88c2.27-2.09 3.665-5.17 3.665-9.17z"
    />
    <path
      fill="#34A853"
      d="M12 24c3.24 0 5.95-1.08 7.93-2.91l-3.88-3.05c-1.08.72-2.45 1.16-4.05 1.16-3.12 0-5.77-2.1-6.72-4.93H1.29v3.15C3.26 21.3 7.35 24 12 24z"
    />
    <path
      fill="#FBBC05"
      d="M5.28 14.27c-.25-.72-.38-1.49-.38-2.27s.13-1.55.38-2.27V6.58H1.29C.47 8.21 0 10.05 0 12s.47 3.79 1.29 5.42l3.99-3.15z"
    />
    <path
      fill="#EA4335"
      d="M12 4.75c1.77 0 3.35.61 4.6 1.8l3.42-3.42C17.95 1.19 15.24 0 12 0 7.35 0 3.26 2.7 1.29 6.58l3.99 3.15c.95-2.83 3.6-4.98 6.72-4.98z"
    />
  </svg>
)

const GitHubIcon: React.FC = () => (
  <svg width="18" height="18" viewBox="0 0 24 24" fill="currentColor" style={{ marginRight: 8, flexShrink: 0 }}>
    <path
      fillRule="evenodd"
      clipRule="evenodd"
      d="M12 2C6.477 2 2 6.484 2 12.017c0 4.425 2.865 8.18 6.839 9.504.5.092.682-.217.682-.483 0-.237-.008-.868-.013-1.703-2.782.605-3.369-1.343-3.369-1.343-.454-1.158-1.11-1.466-1.11-1.466-.908-.62.069-.608.069-.608 1.003.07 1.53 1.032 1.53 1.032.892 1.53 2.341 1.088 2.91.832.092-.647.35-1.088.636-1.338-2.22-.253-4.555-1.113-4.555-4.951 0-1.093.39-1.988 1.029-2.688-.103-.253-.446-1.272.098-2.65 0 0 .84-.27 2.75 1.026A9.564 9.564 0 0112 6.844c.85.004 1.705.115 2.504.337 1.909-1.296 2.747-1.027 2.747-1.027.546 1.379.202 2.398.1 2.651.64.7 1.028 1.595 1.028 2.688 0 3.848-2.339 4.695-4.566 4.943.359.309.678.92.678 1.855 0 1.338-.012 2.419-.012 2.747 0 .268.18.58.688.482A10.019 10.019 0 0022 12.017C22 6.484 17.522 2 12 2z"
    />
  </svg>
)

export const LandingPage: React.FC = () => {
  const { loginWithProvider } = useAuth()
  const [activeTab, setActiveTab] = useState<'kanban' | 'ai' | 'health'>('kanban')

  const scrollToFeatures = () => {
    document.getElementById('features-section')?.scrollIntoView({ behavior: 'smooth' })
  }

  return (
    <Layout
      style={{
        minHeight: '100vh',
        background: '#ffffff',
        color: '#0f172a',
        overflowX: 'hidden',
      }}
    >
      {/* Top Navbar */}
      <Header
        style={{
          position: 'sticky',
          top: 0,
          zIndex: 100,
          background: 'rgba(255, 255, 255, 0.9)',
          backdropFilter: 'blur(16px)',
          borderBottom: '1px solid #e2e8f0',
          padding: '0 32px',
          height: 72,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}
      >
        <Flex align="center" gap={12}>
          <NexusLogo size={36} />
          <Title
            level={3}
            style={{
              margin: 0,
              color: '#0f172a',
              fontSize: 22,
              fontWeight: 700,
              letterSpacing: '-0.5px',
            }}
          >
            TeamNexus
          </Title>
          <Tag color="indigo" style={{ marginLeft: 6, borderRadius: 12 }}>
            v2.0
          </Tag>
        </Flex>

        <Space size="middle">
          <Button
            type="text"
            onClick={scrollToFeatures}
            style={{ color: '#475569', fontWeight: 500 }}
          >
            Tính năng
          </Button>
          <Button
            type="text"
            onClick={() => document.getElementById('architecture-section')?.scrollIntoView({ behavior: 'smooth' })}
            style={{ color: '#475569', fontWeight: 500 }}
          >
            Kiến trúc
          </Button>
          <Button
            type="primary"
            icon={<GitHubIcon />}
            onClick={() => loginWithProvider('github')}
            style={{
              background: 'linear-gradient(135deg, #4f46e5 0%, #7c3aed 100%)',
              border: 'none',
              borderRadius: 8,
              fontWeight: 500,
              height: 38,
              boxShadow: '0 2px 8px rgba(79, 70, 229, 0.25)',
            }}
          >
            Đăng nhập
          </Button>
        </Space>
      </Header>

      <Content style={{ padding: '0 24px' }}>
        {/* HERO SECTION */}
        <section
          style={{
            maxWidth: 1200,
            margin: '0 auto',
            padding: '72px 0 48px',
            textAlign: 'center',
            position: 'relative',
          }}
        >
          {/* Subtle Ambient Light Glow */}
          <div
            style={{
              position: 'absolute',
              top: '5%',
              left: '50%',
              transform: 'translateX(-50%)',
              width: 700,
              height: 400,
              background: 'radial-gradient(circle, rgba(99, 102, 241, 0.12) 0%, rgba(192, 132, 252, 0.08) 45%, transparent 70%)',
              filter: 'blur(70px)',
              pointerEvents: 'none',
              zIndex: 0,
            }}
          />

          <div style={{ position: 'relative', zIndex: 1 }}>
            <Tag
              style={{
                padding: '6px 16px',
                borderRadius: 9999,
                fontSize: 13,
                fontWeight: 600,
                background: '#eef2ff',
                color: '#4f46e5',
                border: '1px solid #c7d2fe',
                marginBottom: 24,
                display: 'inline-flex',
                alignItems: 'center',
                gap: 8,
              }}
            >
              <FireOutlined style={{ color: '#f59e0b' }} />
              TeamNexus 2.0 • Quản trị & Điều phối Dự án Thông minh với AI DeepSeek & Real-time SignalR
            </Tag>

            <Title
              level={1}
              style={{
                fontSize: 'clamp(36px, 5vw, 64px)',
                fontWeight: 800,
                color: '#0f172a',
                letterSpacing: '-1.5px',
                lineHeight: 1.15,
                margin: '0 auto 20px',
                maxWidth: 900,
              }}
            >
              Nâng tầm điều phối công việc với{' '}
              <span
                style={{
                  background: 'linear-gradient(135deg, #4f46e5 0%, #7c3aed 50%, #db2777 100%)',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                }}
              >
                Trí tuệ Nhân tạo & Kanban Thời gian thực
              </span>
            </Title>

            <Paragraph
              style={{
                fontSize: 'clamp(16px, 2vw, 19px)',
                color: '#475569',
                maxWidth: 720,
                margin: '0 auto 36px',
                lineHeight: 1.6,
              }}
            >
              Đồng bộ tác vụ tức thì đa người dùng qua SignalR, tự động dự báo rủi ro tắc nghẽn,
              bóc tách checklist công việc qua AI DeepSeek và bảo đảm trách nhiệm tuyệt đối với AI Accountability Layer.
            </Paragraph>

            {/* Direct Dual Call To Action */}
            <Flex justify="center" gap={16} wrap="wrap" style={{ marginBottom: 48 }}>
              <Button
                type="primary"
                size="large"
                icon={<GitHubIcon />}
                onClick={() => loginWithProvider('github')}
                style={{
                  height: 52,
                  padding: '0 28px',
                  borderRadius: 10,
                  fontSize: 15,
                  fontWeight: 600,
                  background: '#0f172a',
                  color: '#fff',
                  border: 'none',
                  boxShadow: '0 8px 20px -4px rgba(15, 23, 42, 0.3)',
                  display: 'flex',
                  alignItems: 'center',
                }}
              >
                Bắt đầu với GitHub
              </Button>

              <Button
                size="large"
                icon={<GoogleIcon />}
                onClick={() => loginWithProvider('google')}
                style={{
                  height: 52,
                  padding: '0 28px',
                  borderRadius: 10,
                  fontSize: 15,
                  fontWeight: 600,
                  background: '#ffffff',
                  color: '#1e293b',
                  border: '1px solid #cbd5e1',
                  boxShadow: '0 4px 12px rgba(0, 0, 0, 0.05)',
                  display: 'flex',
                  alignItems: 'center',
                }}
              >
                Đăng nhập với Google
              </Button>

              <Button
                size="large"
                type="text"
                onClick={scrollToFeatures}
                icon={<ArrowRightOutlined />}
                style={{
                  height: 52,
                  padding: '0 20px',
                  color: '#475569',
                  fontSize: 15,
                  fontWeight: 500,
                }}
              >
                Xem tính năng
              </Button>
            </Flex>

            {/* INTERACTIVE MOCKUP SHOWCASE (LIGHT MODE) */}
            <div
              style={{
                maxWidth: 1060,
                margin: '0 auto',
                borderRadius: 18,
                border: '1px solid #e2e8f0',
                background: '#ffffff',
                boxShadow: '0 25px 50px -12px rgba(0, 0, 0, 0.08), 0 0 30px rgba(99, 102, 241, 0.05)',
                overflow: 'hidden',
                textAlign: 'left',
              }}
            >
              {/* Mockup Browser Titlebar */}
              <div
                style={{
                  padding: '12px 20px',
                  background: '#f8fafc',
                  borderBottom: '1px solid #e2e8f0',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                }}
              >
                <Flex align="center" gap={8}>
                  <div style={{ width: 12, height: 12, borderRadius: '50%', background: '#ef4444' }} />
                  <div style={{ width: 12, height: 12, borderRadius: '50%', background: '#f59e0b' }} />
                  <div style={{ width: 12, height: 12, borderRadius: '50%', background: '#10b981' }} />
                  <Text style={{ color: '#64748b', fontSize: 13, marginLeft: 12 }}>
                    teamnexus.internal/workspaces/alpha-sprint/board
                  </Text>
                </Flex>

                <Flex gap={8}>
                  <Button
                    size="small"
                    type={activeTab === 'kanban' ? 'primary' : 'text'}
                    onClick={() => setActiveTab('kanban')}
                    style={{ fontSize: 12, borderRadius: 6 }}
                  >
                    Bảng Kanban
                  </Button>
                  <Button
                    size="small"
                    type={activeTab === 'ai' ? 'primary' : 'text'}
                    onClick={() => setActiveTab('ai')}
                    style={{ fontSize: 12, borderRadius: 6 }}
                  >
                    AI Copilot Chat
                  </Button>
                  <Button
                    size="small"
                    type={activeTab === 'health' ? 'primary' : 'text'}
                    onClick={() => setActiveTab('health')}
                    style={{ fontSize: 12, borderRadius: 6 }}
                  >
                    Sức khỏe 92/100
                  </Button>
                </Flex>
              </div>

              {/* Mockup Body Content */}
              <div style={{ padding: '24px', background: '#f8fafc' }}>
                {activeTab === 'kanban' && (
                  <div>
                    <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
                      <Space>
                        <Tag color="purple">Sprint #14 (Real-time SignalR Active)</Tag>
                        <Badge status="processing" text={<span style={{ color: '#059669', fontSize: 12, fontWeight: 500 }}>Đang đồng bộ</span>} />
                      </Space>
                      <Tag color="cyan">8 Nhiệm vụ</Tag>
                    </Flex>
                    <Row gutter={[16, 16]}>
                      <Col xs={24} md={8}>
                        <div style={{ background: '#ffffff', padding: 14, borderRadius: 10, border: '1px solid #e2e8f0', boxShadow: '0 2px 4px rgba(0,0,0,0.02)' }}>
                          <Text strong style={{ color: '#64748b', textTransform: 'uppercase', fontSize: 11, letterSpacing: 1 }}>
                            Cần làm (To Do)
                          </Text>
                          <div style={{ background: '#f8fafc', padding: 12, borderRadius: 8, marginTop: 10, border: '1px solid #e2e8f0' }}>
                            <Tag color="blue" style={{ fontSize: 11 }}>FRONTEND</Tag>
                            <div style={{ color: '#0f172a', fontWeight: 600, margin: '6px 0' }}>Tích hợp SSE Chat AI Stream</div>
                            <Flex justify="space-between" align="center">
                              <Text style={{ color: '#64748b', fontSize: 12 }}><ClockCircleOutlined /> 2 ngày</Text>
                              <Tag color="geekblue">Thịnh T.</Tag>
                            </Flex>
                          </div>
                        </div>
                      </Col>
                      <Col xs={24} md={8}>
                        <div style={{ background: '#ffffff', padding: 14, borderRadius: 10, border: '1px solid #e2e8f0', boxShadow: '0 2px 4px rgba(0,0,0,0.02)' }}>
                          <Text strong style={{ color: '#4f46e5', textTransform: 'uppercase', fontSize: 11, letterSpacing: 1 }}>
                            Đang xử lý (In Progress)
                          </Text>
                          <div style={{ background: '#f8fafc', padding: 12, borderRadius: 8, marginTop: 10, border: '1px solid #818cf8' }}>
                            <Tag color="magenta" style={{ fontSize: 11 }}>AI ENGINE</Tag>
                            <div style={{ color: '#0f172a', fontWeight: 600, margin: '6px 0' }}>Project Health Gauge 0-100</div>
                            <Flex justify="space-between" align="center">
                              <Tag color="green">DeepSeek r1:14b</Tag>
                              <Tag color="geekblue">Nexus Bot</Tag>
                            </Flex>
                          </div>
                        </div>
                      </Col>
                      <Col xs={24} md={8}>
                        <div style={{ background: '#ffffff', padding: 14, borderRadius: 10, border: '1px solid #e2e8f0', boxShadow: '0 2px 4px rgba(0,0,0,0.02)' }}>
                          <Text strong style={{ color: '#059669', textTransform: 'uppercase', fontSize: 11, letterSpacing: 1 }}>
                            Hoàn thành (Done)
                          </Text>
                          <div style={{ background: '#f8fafc', padding: 12, borderRadius: 8, marginTop: 10, border: '1px solid #e2e8f0' }}>
                            <Tag color="green" style={{ fontSize: 11 }}>SECURITY</Tag>
                            <div style={{ color: '#0f172a', fontWeight: 600, margin: '6px 0' }}>Accountability Action Log</div>
                            <Flex justify="space-between" align="center">
                              <Text style={{ color: '#059669', fontSize: 12, fontWeight: 500 }}><CheckCircleFilled /> Đã phê duyệt</Text>
                              <Tag color="purple">Admin</Tag>
                            </Flex>
                          </div>
                        </div>
                      </Col>
                    </Row>
                  </div>
                )}

                {activeTab === 'ai' && (
                  <div style={{ background: '#ffffff', padding: 16, borderRadius: 10, border: '1px solid #e2e8f0' }}>
                    <Flex align="center" gap={10} style={{ marginBottom: 14 }}>
                      <RobotOutlined style={{ fontSize: 20, color: '#4f46e5' }} />
                      <Text strong style={{ color: '#0f172a', fontSize: 15 }}>Nexus AI Task Copilot (DeepSeek Streaming)</Text>
                    </Flex>
                    <div style={{ background: '#f8fafc', padding: 14, borderRadius: 8, borderLeft: '3px solid #4f46e5' }}>
                      <Text style={{ color: '#1e293b', fontSize: 13, lineHeight: 1.6 }}>
                        💡 <strong>Đề xuất phân tách công việc:</strong> Tôi đã phân tích nhiệm vụ &quot;Thiết kế hệ thống báo cáo hiệu suất&quot; thành 3 mục checklist tối ưu:
                      </Text>
                      <div style={{ marginTop: 10, display: 'flex', flexDirection: 'column', gap: 6 }}>
                        <Text style={{ color: '#334155', fontSize: 12 }}>✓ Thiết lập truy vấn PostgreSQL Materialized View cho chỉ số hoàn thành</Text>
                        <Text style={{ color: '#334155', fontSize: 12 }}>✓ Tạo biểu đồ phân bổ thời gian thực hiện bằng Recharts</Text>
                        <Text style={{ color: '#334155', fontSize: 12 }}>✓ Xuất báo cáo PDF tự động hàng tuần qua Hangfire job</Text>
                      </div>
                      <Flex gap={8} style={{ marginTop: 14 }}>
                        <Button size="small" type="primary" style={{ background: '#059669', border: 'none' }}>
                          Phê duyệt & Áp dụng
                        </Button>
                        <Button size="small" style={{ background: '#f1f5f9', color: '#475569', border: '1px solid #cbd5e1' }}>
                          Từ chối
                        </Button>
                      </Flex>
                    </div>
                  </div>
                )}

                {activeTab === 'health' && (
                  <div style={{ background: '#ffffff', padding: 20, borderRadius: 10, border: '1px solid #e2e8f0' }}>
                    <Row gutter={24} align="middle">
                      <Col xs={24} sm={8} style={{ textAlign: 'center' }}>
                        <div style={{ fontSize: 44, fontWeight: 800, color: '#059669' }}>92<span style={{ fontSize: 20, color: '#64748b' }}>/100</span></div>
                        <Tag color="success" style={{ marginTop: 6 }}>Tiến độ rất lành mạnh</Tag>
                      </Col>
                      <Col xs={24} sm={16}>
                        <Text strong style={{ color: '#0f172a', fontSize: 15 }}>Đánh giá rủi ro tự động từ AI Risk Observer:</Text>
                        <Paragraph style={{ color: '#475569', fontSize: 13, margin: '8px 0 0', lineHeight: 1.6 }}>
                          Không có tắc nghẽn nghiêm trọng. Tỷ lệ hoàn thành công việc đúng hạn đạt 94.2%.
                          Dự kiến Sprint 14 sẽ về đích sớm hơn kế hoạch 1 ngày.
                        </Paragraph>
                      </Col>
                    </Row>
                  </div>
                )}
              </div>
            </div>
          </div>
        </section>

        {/* CORE FEATURE PILLARS */}
        <section
          id="features-section"
          style={{
            maxWidth: 1200,
            margin: '64px auto',
            padding: '24px 0',
          }}
        >
          <div style={{ textAlign: 'center', marginBottom: 48 }}>
            <Tag color="cyan" style={{ borderRadius: 12, padding: '4px 12px' }}>
              TÍNH NĂNG VƯỢT TRỘI
            </Tag>
            <Title level={2} style={{ color: '#0f172a', marginTop: 12, fontSize: 32 }}>
              Được thiết kế cho các Đội ngũ Phát triển Hiện đại
            </Title>
            <Paragraph style={{ color: '#64748b', maxWidth: 640, margin: '0 auto' }}>
              Kết hợp hoàn hảo giữa công cụ quản trị Agile trực quan và sức mạnh AI tự động hóa.
            </Paragraph>
          </div>

          <Row gutter={[24, 24]}>
            {/* Feature 1 */}
            <Col xs={24} md={8}>
              <Card
                style={{
                  height: '100%',
                  background: '#ffffff',
                  border: '1px solid #e2e8f0',
                  borderRadius: 16,
                  boxShadow: '0 4px 12px rgba(0, 0, 0, 0.03)',
                }}
              >
                <div
                  style={{
                    width: 48,
                    height: 48,
                    borderRadius: 12,
                    background: '#eef2ff',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    marginBottom: 16,
                  }}
                >
                  <ThunderboltOutlined style={{ fontSize: 24, color: '#4f46e5' }} />
                </div>
                <Title level={4} style={{ color: '#0f172a', marginBottom: 8 }}>
                  Bảng Kanban Thời gian thực
                </Title>
                <Paragraph style={{ color: '#64748b', fontSize: 14, lineHeight: 1.6 }}>
                  Sử dụng SignalR WebSockets kết nối hai chiều. Mọi thao tác kéo thả thẻ công việc,
                  cập nhật trạng thái hoặc bình luận đều hiển thị ngay lập tức tới tất cả đồng đội.
                </Paragraph>
              </Card>
            </Col>

            {/* Feature 2 */}
            <Col xs={24} md={8}>
              <Card
                style={{
                  height: '100%',
                  background: '#ffffff',
                  border: '1px solid #e2e8f0',
                  borderRadius: 16,
                  boxShadow: '0 4px 12px rgba(0, 0, 0, 0.03)',
                }}
              >
                <div
                  style={{
                    width: 48,
                    height: 48,
                    borderRadius: 12,
                    background: '#f5f3ff',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    marginBottom: 16,
                  }}
                >
                  <RobotOutlined style={{ fontSize: 24, color: '#7c3aed' }} />
                </div>
                <Title level={4} style={{ color: '#0f172a', marginBottom: 8 }}>
                  AI Task Copilot (DeepSeek)
                </Title>
                <Paragraph style={{ color: '#64748b', fontSize: 14, lineHeight: 1.6 }}>
                  Trò chuyện trực tiếp với tác vụ qua Server-Sent Events (SSE). Tự động phân tích yêu cầu,
                  bóc tách danh sách việc cần làm (Checklist) và gợi ý giải pháp kỹ thuật tối ưu.
                </Paragraph>
              </Card>
            </Col>

            {/* Feature 3 */}
            <Col xs={24} md={8}>
              <Card
                style={{
                  height: '100%',
                  background: '#ffffff',
                  border: '1px solid #e2e8f0',
                  borderRadius: 16,
                  boxShadow: '0 4px 12px rgba(0, 0, 0, 0.03)',
                }}
              >
                <div
                  style={{
                    width: 48,
                    height: 48,
                    borderRadius: 12,
                    background: '#ecfdf5',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    marginBottom: 16,
                  }}
                >
                  <DashboardOutlined style={{ fontSize: 24, color: '#059669' }} />
                </div>
                <Title level={4} style={{ color: '#0f172a', marginBottom: 8 }}>
                  AI Risk Observer & Health Gauge
                </Title>
                <Paragraph style={{ color: '#64748b', fontSize: 14, lineHeight: 1.6 }}>
                  Đồng hồ đo điểm sức khỏe 0-100 trực quan. AI liên tục quan sát chu kỳ nhiệm vụ,
                  cảnh báo sớm các nguy cơ trễ hạn hoặc quá tải nguồn lực của thành viên.
                </Paragraph>
              </Card>
            </Col>

            {/* Feature 4 */}
            <Col xs={24} md={8}>
              <Card
                style={{
                  height: '100%',
                  background: '#ffffff',
                  border: '1px solid #e2e8f0',
                  borderRadius: 16,
                  boxShadow: '0 4px 12px rgba(0, 0, 0, 0.03)',
                }}
              >
                <div
                  style={{
                    width: 48,
                    height: 48,
                    borderRadius: 12,
                    background: '#fef3c7',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    marginBottom: 16,
                  }}
                >
                  <AppstoreOutlined style={{ fontSize: 24, color: '#d97706' }} />
                </div>
                <Title level={4} style={{ color: '#0f172a', marginBottom: 8 }}>
                  AI Board Templates Thông minh
                </Title>
                <Paragraph style={{ color: '#64748b', fontSize: 14, lineHeight: 1.6 }}>
                  Khởi tạo bảng dự án thần tốc chỉ bằng cách nhập mô tả nhu cầu.
                  AI tự động thiết kế cột quy trình, nhãn phân loại và danh sách công việc khởi điểm.
                </Paragraph>
              </Card>
            </Col>

            {/* Feature 5 */}
            <Col xs={24} md={8}>
              <Card
                style={{
                  height: '100%',
                  background: '#ffffff',
                  border: '1px solid #e2e8f0',
                  borderRadius: 16,
                  boxShadow: '0 4px 12px rgba(0, 0, 0, 0.03)',
                }}
              >
                <div
                  style={{
                    width: 48,
                    height: 48,
                    borderRadius: 12,
                    background: '#fee2e2',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    marginBottom: 16,
                  }}
                >
                  <SafetyCertificateOutlined style={{ fontSize: 24, color: '#dc2626' }} />
                </div>
                <Title level={4} style={{ color: '#0f172a', marginBottom: 8 }}>
                  AI Accountability Layer
                </Title>
                <Paragraph style={{ color: '#64748b', fontSize: 14, lineHeight: 1.6 }}>
                  Đảm bảo trách nhiệm tuyệt đối. Mọi đề xuất thay đổi từ AI đều cần con người phê duyệt
                  (Approve/Reject) và hỗ trợ hoàn tác một chạm (Undo) an toàn, minh bạch.
                </Paragraph>
              </Card>
            </Col>

            {/* Feature 6 */}
            <Col xs={24} md={8}>
              <Card
                style={{
                  height: '100%',
                  background: '#ffffff',
                  border: '1px solid #e2e8f0',
                  borderRadius: 16,
                  boxShadow: '0 4px 12px rgba(0, 0, 0, 0.03)',
                }}
              >
                <div
                  style={{
                    width: 48,
                    height: 48,
                    borderRadius: 12,
                    background: '#e0f2fe',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    marginBottom: 16,
                  }}
                >
                  <UsergroupAddOutlined style={{ fontSize: 24, color: '#0284c7' }} />
                </div>
                <Title level={4} style={{ color: '#0f172a', marginBottom: 8 }}>
                  Quản lý Không gian & Phân quyền RBAC
                </Title>
                <Paragraph style={{ color: '#64748b', fontSize: 14, lineHeight: 1.6 }}>
                  Hỗ trợ nhiều Không gian làm việc (Workspaces), phân cấp vai trò chặt chẽ
                  (Admin, Manager, Member), bảo mật thông tin và quản lý thư mời thành viên tiện lợi.
                </Paragraph>
              </Card>
            </Col>
          </Row>
        </section>

        {/* ARCHITECTURE SECTION */}
        <section
          id="architecture-section"
          style={{
            maxWidth: 1200,
            margin: '48px auto 80px',
            padding: '36px',
            background: 'linear-gradient(135deg, #f8fafc 0%, #f1f5f9 100%)',
            border: '1px solid #e2e8f0',
            borderRadius: 20,
          }}
        >
          <Row gutter={[32, 24]} align="middle">
            <Col xs={24} md={12}>
              <Tag color="purple" style={{ borderRadius: 12 }}>KIẾN TRÚC HIỆN ĐẠI</Tag>
              <Title level={2} style={{ color: '#0f172a', marginTop: 12 }}>
                Sức mạnh từ .NET 10 & Modular Monolith
              </Title>
              <Paragraph style={{ color: '#475569', fontSize: 15, lineHeight: 1.7 }}>
                TeamNexus được xây dựng dựa trên tiêu chuẩn kiến trúc phần mềm doanh nghiệp khắt khe.
                Tách biệt các module ranh giới (Identity, Workspace, Task, Board, AI, Reporting),
                vận hành ổn định với hiệu năng cao và khả năng mở rộng không giới hạn.
              </Paragraph>
              <Space size={[8, 12]} wrap style={{ marginTop: 12 }}>
                <Tag icon={<CodeOutlined />} color="blue">.NET 10 LTS</Tag>
                <Tag icon={<ApartmentOutlined />} color="geekblue">Modular Monolith</Tag>
                <Tag icon={<ThunderboltOutlined />} color="gold">SignalR WebSockets</Tag>
                <Tag icon={<LockOutlined />} color="green">PostgreSQL & EF Core</Tag>
                <Tag icon={<HistoryOutlined />} color="purple">React 19 & Vite</Tag>
                <Tag icon={<RobotOutlined />} color="magenta">DeepSeek AI Engine</Tag>
              </Space>
            </Col>

            <Col xs={24} md={12}>
              <div
                style={{
                  background: '#0f172a',
                  padding: 24,
                  borderRadius: 14,
                  border: '1px solid #1e293b',
                  fontFamily: 'monospace',
                  fontSize: 13,
                  color: '#94a3b8',
                  boxShadow: '0 10px 25px rgba(0,0,0,0.1)',
                }}
              >
                <div style={{ color: '#818cf8', marginBottom: 6 }}>// TeamNexus Modular Architecture</div>
                <div style={{ color: '#e2e8f0' }}>src/</div>
                <div>├── TeamNexus.Identity/ <span style={{ color: '#64748b' }}>// OAuth Google, GitHub, JWT</span></div>
                <div>├── TeamNexus.Workspaces/ <span style={{ color: '#64748b' }}>// RBAC, Members, Invitations</span></div>
                <div>├── TeamNexus.Tasks/ <span style={{ color: '#64748b' }}>// Kanban Columns, Cards, SignalR</span></div>
                <div>├── TeamNexus.AI/ <span style={{ color: '#34d399' }}>// Copilot, Observer, SSE Streaming</span></div>
                <div>└── TeamNexus.Reporting/ <span style={{ color: '#64748b' }}>// Metrics, Health Gauge, Logs</span></div>
              </div>
            </Col>
          </Row>
        </section>

        {/* BOTTOM CTA */}
        <section
          style={{
            maxWidth: 800,
            margin: '0 auto 80px',
            textAlign: 'center',
            padding: '48px 24px',
            borderRadius: 20,
            background: 'linear-gradient(135deg, rgba(79, 70, 229, 0.06) 0%, rgba(147, 51, 234, 0.08) 100%)',
            border: '1px solid rgba(129, 140, 248, 0.3)',
          }}
        >
          <Title level={2} style={{ color: '#0f172a', marginBottom: 12 }}>
            Sẵn sàng nâng tầm năng suất làm việc nhóm?
          </Title>
          <Paragraph style={{ color: '#475569', fontSize: 16, marginBottom: 28 }}>
            Đăng nhập ngay hôm nay hoàn toàn miễn phí qua tài khoản GitHub hoặc Google.
          </Paragraph>
          <Flex justify="center" gap={16} wrap="wrap">
            <Button
              type="primary"
              size="large"
              icon={<GitHubIcon />}
              onClick={() => loginWithProvider('github')}
              style={{
                height: 48,
                padding: '0 24px',
                borderRadius: 8,
                background: '#0f172a',
                color: '#fff',
                border: 'none',
                fontWeight: 600,
                boxShadow: '0 4px 12px rgba(15, 23, 42, 0.2)',
              }}
            >
              Đăng nhập với GitHub
            </Button>
            <Button
              size="large"
              icon={<GoogleIcon />}
              onClick={() => loginWithProvider('google')}
              style={{
                height: 48,
                padding: '0 24px',
                borderRadius: 8,
                background: '#fff',
                color: '#1e293b',
                border: '1px solid #cbd5e1',
                fontWeight: 600,
                boxShadow: '0 2px 8px rgba(0,0,0,0.05)',
              }}
            >
              Đăng nhập với Google
            </Button>
          </Flex>
        </section>
      </Content>

      {/* FOOTER */}
      <Footer
        style={{
          background: '#f8fafc',
          borderTop: '1px solid #e2e8f0',
          padding: '24px 32px',
          textAlign: 'center',
        }}
      >
        <Flex justify="space-between" align="center" wrap="wrap" gap={12} style={{ maxWidth: 1200, margin: '0 auto' }}>
          <Text style={{ color: '#64748b', fontSize: 13 }}>
            © 2026 TeamNexus — Hệ thống Điều phối Không gian làm việc Thông minh.
          </Text>
          <Space size="middle">
            <Text style={{ color: '#64748b', fontSize: 13 }}>Phiên bản 2.0.0</Text>
            <Text style={{ color: '#64748b', fontSize: 13 }}>•</Text>
            <Text style={{ color: '#64748b', fontSize: 13 }}>Real-time SignalR</Text>
            <Text style={{ color: '#64748b', fontSize: 13 }}>•</Text>
            <Text style={{ color: '#64748b', fontSize: 13 }}>DeepSeek AI</Text>
          </Space>
        </Flex>
      </Footer>
    </Layout>
  )
}
