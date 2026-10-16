import React, { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import {
  AuditOutlined,
  BranchesOutlined,
  CheckCircleFilled,
  CheckCircleOutlined,
  CheckOutlined,
  ClockCircleOutlined,
  CodeOutlined,
  DatabaseOutlined,
  DownOutlined,
  FileTextOutlined,
  LineChartOutlined,
  LockOutlined,
  MobileOutlined,
  ProjectOutlined,
  RobotOutlined,
  SafetyCertificateOutlined,
  SyncOutlined,
  ThunderboltOutlined,
  UndoOutlined,
} from '@ant-design/icons'
import {
  Badge,
  Button,
  Card,
  Col,
  Flex,
  Layout,
  Progress,
  Row,
  Space,
  Tag,
  Typography,
} from 'antd'
import { useAuth } from '../../auth/hooks/useAuth'
import { NexusLogo } from '../../../shared/components/NexusLogo'

const { Header, Content, Footer } = Layout
const { Title, Paragraph, Text } = Typography

export const GoogleIcon: React.FC = () => (
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

export const GitHubIcon: React.FC = () => (
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
  const navigate = useNavigate()

  // State for Interactive Command Center Showcase
  const [activeShowcaseTab, setActiveShowcaseTab] = useState<'triage' | 'kanban' | 'accountability' | 'health'>('triage')
  const [confidenceMode, setConfidenceMode] = useState<'high' | 'medium' | 'low'>('high')
  const [demoActionStatus, setDemoActionStatus] = useState<'pending' | 'approved' | 'undone'>('pending')

  const scrollToSection = (id: string) => {
    document.getElementById(id)?.scrollIntoView({ behavior: 'smooth' })
  }

  return (
    <Layout
      style={{
        minHeight: '100vh',
        background: '#ffffff',
        color: '#0f172a',
        overflowX: 'hidden',
        fontFamily: "'Be Vietnam Pro', sans-serif",
      }}
    >
      {/* 1. Sticky Glassmorphic Navbar */}
      <Header
        style={{
          position: 'sticky',
          top: 0,
          zIndex: 100,
          background: 'rgba(255, 255, 255, 0.88)',
          backdropFilter: 'blur(16px)',
          WebkitBackdropFilter: 'blur(16px)',
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
              fontWeight: 800,
              letterSpacing: '-0.5px',
            }}
          >
            TeamNexus
          </Title>
          <Tag
            style={{
              marginLeft: 4,
              borderRadius: 9999,
              fontSize: 11,
              fontWeight: 600,
              color: '#7c3aed',
              backgroundColor: '#f5f3ff',
              borderColor: '#ddd6fe',
              fontFamily: "'JetBrains Mono', monospace",
              padding: '2px 8px',
            }}
          >
            v4.2 • Hybrid AI
          </Tag>
        </Flex>

        <Space size="middle" className="landing-nav-links">
          <Button
            type="text"
            onClick={() => scrollToSection('interactive-showcase')}
            style={{ color: '#475569', fontWeight: 500 }}
          >
            Trải nghiệm tương tác
          </Button>
          <Button
            type="text"
            onClick={() => scrollToSection('hybrid-ai-section')}
            style={{ color: '#475569', fontWeight: 500 }}
          >
            Hybrid AI
          </Button>
          <Button
            type="text"
            onClick={() => scrollToSection('accountability-agents-section')}
            style={{ color: '#475569', fontWeight: 500 }}
          >
            Giải trình & AI Agents
          </Button>
          <Button
            type="text"
            onClick={() => scrollToSection('architecture-ecosystem-section')}
            style={{ color: '#475569', fontWeight: 500 }}
          >
            Kiến trúc & Đa nền tảng
          </Button>
        </Space>

        <Space size="small">
          <Button
            type="default"
            onClick={() => navigate('/login')}
            style={{
              borderColor: '#cbd5e1',
              color: '#334155',
              fontWeight: 600,
              borderRadius: 8,
              height: 40,
              padding: '0 16px',
            }}
          >
            Đăng nhập
          </Button>
          <Button
            type="primary"
            icon={<GitHubIcon />}
            onClick={() => loginWithProvider('github')}
            style={{
              background: 'linear-gradient(135deg, #4f46e5 0%, #7c3aed 100%)',
              border: 'none',
              borderRadius: 8,
              fontWeight: 600,
              height: 40,
              padding: '0 18px',
              boxShadow: '0 4px 14px rgba(79, 70, 229, 0.3)',
            }}
          >
            Bắt đầu với GitHub
          </Button>
        </Space>
      </Header>

      <Content>
        {/* 2. Hero Section & Live Metrics Strip */}
        <div
          style={{
            position: 'relative',
            padding: '80px 24px 60px',
            background: 'radial-gradient(ellipse 80% 50% at 50% -10%, rgba(124, 58, 237, 0.12), transparent 70%), linear-gradient(180deg, #ffffff 0%, #f8fafc 100%)',
            textAlign: 'center',
          }}
        >
          <div style={{ maxWidth: 1040, margin: '0 auto' }}>
            <Flex justify="center" style={{ marginBottom: 20 }}>
              <Tag
                style={{
                  padding: '6px 16px',
                  borderRadius: 9999,
                  background: 'linear-gradient(135deg, #f5f3ff 0%, #eef2ff 100%)',
                  border: '1px solid #c7d2fe',
                  color: '#4338ca',
                  fontWeight: 600,
                  fontSize: 13,
                  display: 'inline-flex',
                  alignItems: 'center',
                  gap: 6,
                }}
              >
                <span>✨</span>Kiến trúc Hybrid AI Thế hệ mới
              </Tag>
            </Flex>

            <Title
              level={1}
              style={{
                fontSize: 'clamp(2.4rem, 5vw, 3.8rem)',
                fontWeight: 800,
                lineHeight: 1.18,
                letterSpacing: '-1.5px',
                color: '#0f172a',
                marginBottom: 24,
              }}
            >
              Điều phối Không gian Làm việc Thông minh với{' '}
              <span
                style={{
                  background: 'linear-gradient(135deg, #4f46e5 0%, #7c3aed 100%)',
                  WebkitBackgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                }}
              >
                Hybrid AI & Lớp Giải trình Minh bạch
              </span>
            </Title>

            <Paragraph
              style={{
                fontSize: 18,
                color: '#475569',
                maxWidth: 820,
                margin: '0 auto 36px',
                lineHeight: 1.65,
              }}
            >
              Giải quyết triệt để bài toán &quot;máy ghi chép thụ động&quot; và rủi ro &quot;hộp đen AI&quot; bằng sự kết hợp giữa{' '}
              <strong style={{ color: '#0f172a' }}>Jev 1.13 System-1 (&lt;300ms Decisions API)</strong>,{' '}
              <strong style={{ color: '#0f172a' }}>DeepSeek System-2 (SSE Reasoning)</strong>,{' '}
              <strong style={{ color: '#0f172a' }}>Đội ngũ AI Agent Thực thi</strong> và{' '}
              <strong style={{ color: '#0f172a' }}>Kanban Thời gian thực qua SignalR</strong>.
            </Paragraph>

            {/* Dual OAuth CTA Buttons */}
            <Flex justify="center" gap={16} wrap="wrap" style={{ marginBottom: 44 }}>
              <Button
                type="primary"
                size="large"
                icon={<GitHubIcon />}
                onClick={() => loginWithProvider('github')}
                style={{
                  height: 52,
                  padding: '0 28px',
                  fontSize: 16,
                  fontWeight: 600,
                  borderRadius: 10,
                  background: 'linear-gradient(135deg, #4f46e5 0%, #7c3aed 100%)',
                  border: 'none',
                  boxShadow: '0 8px 24px rgba(79, 70, 229, 0.35)',
                }}
              >
                Bắt đầu miễn phí với GitHub
              </Button>
              <Button
                size="large"
                icon={<GoogleIcon />}
                onClick={() => loginWithProvider('google')}
                style={{
                  height: 52,
                  padding: '0 28px',
                  fontSize: 16,
                  fontWeight: 600,
                  borderRadius: 10,
                  borderColor: '#cbd5e1',
                  background: '#ffffff',
                  color: '#1e293b',
                  boxShadow: '0 2px 8px rgba(15, 23, 42, 0.06)',
                }}
              >
                Tiếp tục với Google
              </Button>
              <Button
                type="dashed"
                size="large"
                icon={<DownOutlined />}
                onClick={() => scrollToSection('interactive-showcase')}
                style={{
                  height: 52,
                  padding: '0 22px',
                  fontSize: 15,
                  fontWeight: 600,
                  borderRadius: 10,
                  color: '#4f46e5',
                  borderColor: '#c7d2fe',
                  background: '#f5f3ff',
                }}
              >
                Khám phá Command Center ↓
              </Button>
            </Flex>

            {/* Dải 5 Chỉ Số Thực Chứng (Engineering Proof Strip) */}
            <Card
              style={{
                borderRadius: 14,
                border: '1px solid #e2e8f0',
                background: 'rgba(255, 255, 255, 0.95)',
                boxShadow: '0 12px 30px rgba(15, 23, 42, 0.05)',
                backdropFilter: 'blur(8px)',
              }}
              styles={{ body: { padding: '20px 24px' } }}
            >
              <Row gutter={[24, 20]} align="middle" justify="center">
                <Col xs={12} sm={8} md={4} lg={4}>
                  <Text
                    style={{
                      fontFamily: "'JetBrains Mono', monospace",
                      fontSize: 24,
                      fontWeight: 800,
                      color: '#4f46e5',
                      display: 'block',
                      lineHeight: 1.2,
                    }}
                  >
                    &lt; 300ms
                  </Text>
                  <Text style={{ fontSize: 12, color: '#64748b', fontWeight: 500 }}>
                    Jev System-1 Triage
                  </Text>
                </Col>
                <Col xs={12} sm={8} md={4} lg={4}>
                  <Text
                    style={{
                      fontFamily: "'JetBrains Mono', monospace",
                      fontSize: 24,
                      fontWeight: 800,
                      color: '#10b981',
                      display: 'block',
                      lineHeight: 1.2,
                    }}
                  >
                    100%
                  </Text>
                  <Text style={{ fontSize: 12, color: '#64748b', fontWeight: 500 }}>
                    Human-in-the-Loop &amp; Undo
                  </Text>
                </Col>
                <Col xs={12} sm={8} md={5} lg={5}>
                  <Text
                    style={{
                      fontFamily: "'JetBrains Mono', monospace",
                      fontSize: 24,
                      fontWeight: 800,
                      color: '#7c3aed',
                      display: 'block',
                      lineHeight: 1.2,
                    }}
                  >
                    912 BE | 738 FE
                  </Text>
                  <Text style={{ fontSize: 12, color: '#64748b', fontWeight: 500 }}>
                    Automated Quality Gates
                  </Text>
                </Col>
                <Col xs={12} sm={8} md={4} lg={4}>
                  <Text
                    style={{
                      fontFamily: "'JetBrains Mono', monospace",
                      fontSize: 24,
                      fontWeight: 800,
                      color: '#0284c7',
                      display: 'block',
                      lineHeight: 1.2,
                    }}
                  >
                    14 Migrations
                  </Text>
                  <Text style={{ fontSize: 12, color: '#64748b', fontWeight: 500 }}>
                    PostgreSQL 16 + pgvector
                  </Text>
                </Col>
                <Col xs={12} sm={8} md={4} lg={4}>
                  <Text
                    style={{
                      fontFamily: "'JetBrains Mono', monospace",
                      fontSize: 24,
                      fontWeight: 800,
                      color: '#e11d48',
                      display: 'block',
                      lineHeight: 1.2,
                    }}
                  >
                    Real-time
                  </Text>
                  <Text style={{ fontSize: 12, color: '#64748b', fontWeight: 500 }}>
                    ASP.NET Core SignalR
                  </Text>
                </Col>
              </Row>
            </Card>
          </div>
        </div>

        {/* 3. Interactive Command Center (4 Kịch bản Trải nghiệm Trực tiếp) */}
        <div
          id="interactive-showcase"
          style={{
            padding: '70px 24px',
            background: '#f8fafc',
            borderTop: '1px solid #e2e8f0',
            borderBottom: '1px solid #e2e8f0',
          }}
        >
          <div style={{ maxWidth: 1160, margin: '0 auto' }}>
            <div style={{ textAlign: 'center', marginBottom: 40 }}>
              <Tag color="purple" style={{ borderRadius: 9999, fontWeight: 600, padding: '3px 12px' }}>
                LIVE INTERACTION SHOWCASE
              </Tag>
              <Title level={2} style={{ fontSize: 32, fontWeight: 800, color: '#0f172a', margin: '12px 0 8px' }}>
                Trải Nghiệm Trung Tâm Điều Phối Command Center
              </Title>
              <Paragraph style={{ color: '#64748b', fontSize: 16, maxWidth: 680, margin: '0 auto' }}>
                Bấm tương tác trực tiếp với 4 phân hệ cốt lõi để kiểm chứng tốc độ, độ chính xác và tính an toàn của hệ thống.
              </Paragraph>
            </div>

            {/* Chrome Window Mockup */}
            <div
              style={{
                borderRadius: 16,
                border: '1px solid #cbd5e1',
                background: '#0f172a',
                boxShadow: '0 20px 40px -15px rgba(15, 23, 42, 0.25)',
                overflow: 'hidden',
              }}
            >
              {/* Window Header */}
              <div
                style={{
                  background: '#1e293b',
                  padding: '12px 20px',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'space-between',
                  borderBottom: '1px solid #334155',
                }}
              >
                <Flex align="center" gap={8}>
                  <div style={{ width: 12, height: 12, borderRadius: '50%', background: '#ef4444' }} />
                  <div style={{ width: 12, height: 12, borderRadius: '50%', background: '#f59e0b' }} />
                  <div style={{ width: 12, height: 12, borderRadius: '50%', background: '#10b981' }} />
                  <span
                    style={{
                      fontFamily: "'JetBrains Mono', monospace",
                      fontSize: 12,
                      color: '#94a3b8',
                      marginLeft: 12,
                    }}
                  >
                    app.teamnexus.cloud/command-center
                  </span>
                </Flex>
                <Tag
                  style={{
                    backgroundColor: 'rgba(16, 185, 129, 0.15)',
                    borderColor: '#059669',
                    color: '#34d399',
                    borderRadius: 9999,
                    fontFamily: "'JetBrains Mono', monospace",
                    fontSize: 11,
                  }}
                >
                  ● SignalR Connected (14ms)
                </Tag>
              </div>

              {/* Showcase Tab Nav */}
              <div
                style={{
                  background: '#0f172a',
                  padding: '12px 20px 0',
                  borderBottom: '1px solid #1e293b',
                  display: 'flex',
                  gap: 8,
                  overflowX: 'auto',
                }}
              >
                <Button
                  type={activeShowcaseTab === 'triage' ? 'primary' : 'text'}
                  onClick={() => setActiveShowcaseTab('triage')}
                  icon={<ThunderboltOutlined />}
                  style={{
                    background: activeShowcaseTab === 'triage' ? '#4f46e5' : 'transparent',
                    color: activeShowcaseTab === 'triage' ? '#ffffff' : '#94a3b8',
                    fontWeight: 600,
                    borderRadius: '8px 8px 0 0',
                    borderBottom: 'none',
                    height: 40,
                  }}
                >
                  ⚡ Jev &lt;300ms Triage
                </Button>
                <Button
                  type={activeShowcaseTab === 'kanban' ? 'primary' : 'text'}
                  onClick={() => setActiveShowcaseTab('kanban')}
                  icon={<ProjectOutlined />}
                  style={{
                    background: activeShowcaseTab === 'kanban' ? '#4f46e5' : 'transparent',
                    color: activeShowcaseTab === 'kanban' ? '#ffffff' : '#94a3b8',
                    fontWeight: 600,
                    borderRadius: '8px 8px 0 0',
                    borderBottom: 'none',
                    height: 40,
                  }}
                >
                  📋 Kanban &amp; AI Agent
                </Button>
                <Button
                  type={activeShowcaseTab === 'accountability' ? 'primary' : 'text'}
                  onClick={() => setActiveShowcaseTab('accountability')}
                  icon={<SafetyCertificateOutlined />}
                  style={{
                    background: activeShowcaseTab === 'accountability' ? '#4f46e5' : 'transparent',
                    color: activeShowcaseTab === 'accountability' ? '#ffffff' : '#94a3b8',
                    fontWeight: 600,
                    borderRadius: '8px 8px 0 0',
                    borderBottom: 'none',
                    height: 40,
                  }}
                >
                  🛡️ Accountability Layer
                </Button>
                <Button
                  type={activeShowcaseTab === 'health' ? 'primary' : 'text'}
                  onClick={() => setActiveShowcaseTab('health')}
                  icon={<LineChartOutlined />}
                  style={{
                    background: activeShowcaseTab === 'health' ? '#4f46e5' : 'transparent',
                    color: activeShowcaseTab === 'health' ? '#ffffff' : '#94a3b8',
                    fontWeight: 600,
                    borderRadius: '8px 8px 0 0',
                    borderBottom: 'none',
                    height: 40,
                  }}
                >
                  📊 Health &amp; Auto-Healing
                </Button>
              </div>

              {/* Showcase Body Content */}
              <div style={{ padding: '28px 24px', background: '#0b1120', minHeight: 380 }}>
                {/* Tab 1: Jev <300ms Smart Triage & Dispatch */}
                {activeShowcaseTab === 'triage' && (
                  <div>
                    <Row gutter={[24, 24]}>
                      <Col xs={24} lg={11}>
                        <div
                          style={{
                            background: '#1e293b',
                            borderRadius: 12,
                            padding: 20,
                            border: '1px solid #334155',
                          }}
                        >
                          <Text style={{ color: '#94a3b8', fontSize: 13, textTransform: 'uppercase', letterSpacing: 0.5 }}>
                            Kịch bản Nhiệm vụ Đầu vào (Input Task)
                          </Text>
                          <div style={{ marginTop: 10, color: '#f8fafc', fontWeight: 600, fontSize: 15 }}>
                            #TN-204: Tối ưu hoá truy vấn pgvector HNSW và phân tích dung lượng bộ nhớ
                          </div>
                          <Paragraph style={{ color: '#94a3b8', fontSize: 13, marginTop: 6, lineHeight: 1.5 }}>
                            Mô tả: Lập chỉ mục HNSW cho 500k chunks tài liệu trên PostgreSQL 16, đảm bảo P99 &lt; 50ms và kiểm tra giới hạn kết nối qua pooling.
                          </Paragraph>

                          <div style={{ marginTop: 18, borderTop: '1px solid #334155', paddingTop: 16 }}>
                            <Text style={{ color: '#cbd5e1', fontSize: 13, fontWeight: 600, display: 'block', marginBottom: 10 }}>
                              Chọn Mức Độ Tin Cậy Của Jev 1.13:
                            </Text>
                            <Flex gap={8} wrap="wrap">
                              <Button
                                size="small"
                                type={confidenceMode === 'high' ? 'primary' : 'default'}
                                onClick={() => setConfidenceMode('high')}
                                style={{
                                  borderRadius: 6,
                                  fontWeight: 600,
                                  background: confidenceMode === 'high' ? '#10b981' : '#334155',
                                  borderColor: confidenceMode === 'high' ? '#10b981' : '#475569',
                                  color: '#ffffff',
                                }}
                              >
                                Cao (94% ≥ 0.85)
                              </Button>
                              <Button
                                size="small"
                                type={confidenceMode === 'medium' ? 'primary' : 'default'}
                                onClick={() => setConfidenceMode('medium')}
                                style={{
                                  borderRadius: 6,
                                  fontWeight: 600,
                                  background: confidenceMode === 'medium' ? '#f59e0b' : '#334155',
                                  borderColor: confidenceMode === 'medium' ? '#f59e0b' : '#475569',
                                  color: '#ffffff',
                                }}
                              >
                                Trung bình (68%)
                              </Button>
                              <Button
                                size="small"
                                type={confidenceMode === 'low' ? 'primary' : 'default'}
                                onClick={() => setConfidenceMode('low')}
                                style={{
                                  borderRadius: 6,
                                  fontWeight: 600,
                                  background: confidenceMode === 'low' ? '#ef4444' : '#334155',
                                  borderColor: confidenceMode === 'low' ? '#ef4444' : '#475569',
                                  color: '#ffffff',
                                }}
                              >
                                Thấp (38% &lt; 0.50)
                              </Button>
                            </Flex>
                          </div>
                        </div>
                      </Col>

                      <Col xs={24} lg={13}>
                        <div
                          style={{
                            background: '#131d31',
                            borderRadius: 12,
                            padding: 20,
                            border: '1px solid #3b82f6',
                            boxShadow: '0 0 20px rgba(59, 130, 246, 0.15)',
                          }}
                        >
                          <Flex justify="space-between" align="center" style={{ marginBottom: 14 }}>
                            <Text style={{ color: '#60a5fa', fontWeight: 700, fontSize: 14 }}>
                              ĐẦU RA ĐỊNH KIỂU JEV 1.13 (/api/alpha/decisions)
                            </Text>
                            <Tag color="cyan" style={{ fontFamily: "'JetBrains Mono', monospace" }}>
                              {confidenceMode === 'high' ? '185ms' : confidenceMode === 'medium' ? '210ms' : '160ms'} Latency
                            </Tag>
                          </Flex>

                          {confidenceMode === 'high' && (
                            <div>
                              <Tag color="success" style={{ marginBottom: 12, fontWeight: 600 }}>
                                Strategy: DirectAssign (Tự động gán trực tiếp)
                              </Tag>
                              <div style={{ color: '#e2e8f0', fontSize: 13, lineHeight: 1.8 }}>
                                <div><strong>Choice.Domain:</strong> <span style={{ color: '#38bdf8' }}>Backend (.NET 10 / EF Core)</span></div>
                                <div><strong>Choice.WorkType:</strong> <span style={{ color: '#38bdf8' }}>Feature &amp; Optimization</span></div>
                                <div><strong>Score.Priority:</strong> <span style={{ color: '#34d399' }}>High (Score: 0.88)</span></div>
                                <div><strong>Noul.NeedsSecurityReview:</strong> <span style={{ color: '#fcd34d' }}>true (Chạm tầng CSDL)</span></div>
                                <div><strong>Noul.TargetRelease:</strong> <span style={{ color: '#a78bfa' }}>&quot;Sprint 42&quot;</span></div>
                                <div style={{ marginTop: 12, padding: '10px 14px', background: '#1e293b', borderRadius: 8, borderLeft: '4px solid #10b981' }}>
                                  <Text style={{ color: '#cbd5e1', fontSize: 13 }}>
                                    🎯 <strong>Người nhận việc đề xuất:</strong> Lê Hoàng Anh (Tech Lead) — Skills: .NET 10, C#, PostgreSQL — WIP: 2/5 (Tải tối ưu)
                                  </Text>
                                </div>
                              </div>
                            </div>
                          )}

                          {confidenceMode === 'medium' && (
                            <div>
                              <Tag color="warning" style={{ marginBottom: 12, fontWeight: 600 }}>
                                Strategy: OfferChoices (Đề xuất Top 2–3 ứng viên để Lead duyệt)
                              </Tag>
                              <div style={{ color: '#e2e8f0', fontSize: 13, lineHeight: 1.8 }}>
                                <div><strong>Choice.Domain:</strong> <span style={{ color: '#38bdf8' }}>Fullstack</span></div>
                                <div><strong>Choice.WorkType:</strong> <span style={{ color: '#38bdf8' }}>Enhancement</span></div>
                                <div><strong>Score.Priority:</strong> <span style={{ color: '#fbbf24' }}>Medium (Score: 0.64)</span></div>
                                <div><strong>Noul.NeedsSecurityReview:</strong> <span style={{ color: '#94a3b8' }}>false</span></div>
                                <div style={{ marginTop: 12, padding: '10px 14px', background: '#1e293b', borderRadius: 8, borderLeft: '4px solid #f59e0b' }}>
                                  <Text style={{ color: '#cbd5e1', fontSize: 13 }}>
                                    👥 <strong>Top 2 ứng viên phù hợp:</strong>
                                    <br />1. Nguyễn Minh Trí (Senior Fullstack) — Độ khớp 82% — WIP: 3/5
                                    <br />2. Trần Thu Hà (Frontend Lead) — Độ khớp 74% — WIP: 1/4
                                  </Text>
                                </div>
                              </div>
                            </div>
                          )}

                          {confidenceMode === 'low' && (
                            <div>
                              <Tag color="error" style={{ marginBottom: 12, fontWeight: 600 }}>
                                Strategy: ManualRequired (Fallback — Yêu cầu gán thủ công)
                              </Tag>
                              <div style={{ color: '#e2e8f0', fontSize: 13, lineHeight: 1.8 }}>
                                <div><strong>Choice.Domain:</strong> <span style={{ color: '#94a3b8' }}>Uncertain / Complex</span></div>
                                <div><strong>Choice.WorkType:</strong> <span style={{ color: '#94a3b8' }}>Task</span></div>
                                <div><strong>Score.Priority:</strong> <span style={{ color: '#94a3b8' }}>Normal (Score: 0.35)</span></div>
                                <div><strong>Noul.RequiresClarification:</strong> <span style={{ color: '#f87171' }}>true</span></div>
                                <div style={{ marginTop: 12, padding: '10px 14px', background: '#1e293b', borderRadius: 8, borderLeft: '4px solid #ef4444' }}>
                                  <Text style={{ color: '#fca5a5', fontSize: 13 }}>
                                    ⚠️ <strong>Cảnh báo:</strong> Độ tự tin dưới ngưỡng an toàn (0.50). Hệ thống không tự ý gán để tránh quá tải WIP hoặc phân bổ sai kỹ năng.
                                  </Text>
                                </div>
                              </div>
                            </div>
                          )}
                        </div>
                      </Col>
                    </Row>
                  </div>
                )}

                {/* Tab 2: Real-time Kanban & AI Co-workers */}
                {activeShowcaseTab === 'kanban' && (
                  <div>
                    <Row gutter={[16, 16]}>
                      <Col xs={24} md={8}>
                        <div style={{ background: '#1e293b', borderRadius: 10, padding: 14, minHeight: 310, border: '1px solid #334155' }}>
                          <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
                            <Text style={{ color: '#cbd5e1', fontWeight: 700, fontSize: 13 }}>TO DO (1)</Text>
                            <Badge count={1} style={{ backgroundColor: '#475569' }} />
                          </Flex>
                          <div style={{ background: '#0f172a', borderRadius: 8, padding: 12, border: '1px solid #334155' }}>
                            <Tag color="blue" style={{ fontSize: 11, marginBottom: 6 }}>#TN-102</Tag>
                            <div style={{ color: '#f8fafc', fontWeight: 600, fontSize: 13 }}>Tích hợp OAuth PKCE Flow</div>
                            <Text style={{ color: '#94a3b8', fontSize: 12, display: 'block', marginTop: 4 }}>WIP Limit: 1/4</Text>
                          </div>
                        </div>
                      </Col>

                      <Col xs={24} md={8}>
                        <div
                          style={{
                            background: '#1e293b',
                            borderRadius: 10,
                            padding: 14,
                            minHeight: 310,
                            border: '2px solid #7c3aed',
                            boxShadow: '0 0 15px rgba(124, 58, 237, 0.25)',
                          }}
                        >
                          <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
                            <Text style={{ color: '#c084fc', fontWeight: 700, fontSize: 13 }}>
                              ⚡ IN PROGRESS (AI AGENT ACTIVE)
                            </Text>
                            <Tag color="purple" style={{ margin: 0, fontSize: 10 }}>Live ReAct</Tag>
                          </Flex>
                          <div style={{ background: 'linear-gradient(135deg, #1e1b4b 0%, #0f172a 100%)', borderRadius: 8, padding: 14, border: '1px solid #7c3aed' }}>
                            <Flex justify="space-between" align="center">
                              <Tag color="purple" style={{ fontSize: 11 }}>#TN-104</Tag>
                              <Tag color="geekblue" style={{ fontSize: 11 }}>@tech_lead Agent</Tag>
                            </Flex>
                            <div style={{ color: '#ffffff', fontWeight: 600, fontSize: 13, marginTop: 6 }}>
                              Tối ưu hóa truy vấn pgvector HNSW
                            </div>
                            <div style={{ marginTop: 10, padding: 8, background: 'rgba(0, 0, 0, 0.4)', borderRadius: 6, fontSize: 11, color: '#e2e8f0', fontFamily: "'JetBrains Mono', monospace" }}>
                              <div>&gt; [Tool 1/4] InspectMemberWorkload: OK</div>
                              <div>&gt; [Tool 2/4] MatchSkills(PostgreSQL): 96%</div>
                              <div>&gt; [Tool 3/4] ValidateAcceptanceCriteria: Pass</div>
                              <div style={{ color: '#34d399' }}>&gt; [Tool 4/4] ProposeExecutionPlan: Ready</div>
                            </div>
                            <div style={{ marginTop: 10 }}>
                              <Flex justify="space-between" style={{ fontSize: 11, color: '#c084fc', marginBottom: 4 }}>
                                <span>ReAct Guardrail</span>
                                <span>3/15 tool-calls • &lt; 50k tokens</span>
                              </Flex>
                              <Progress percent={20} size="small" strokeColor="#7c3aed" showInfo={false} />
                            </div>
                          </div>
                        </div>
                      </Col>

                      <Col xs={24} md={8}>
                        <div style={{ background: '#1e293b', borderRadius: 10, padding: 14, minHeight: 310, border: '1px solid #334155' }}>
                          <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
                            <Text style={{ color: '#cbd5e1', fontWeight: 700, fontSize: 13 }}>DONE (1)</Text>
                            <Badge count={1} style={{ backgroundColor: '#10b981' }} />
                          </Flex>
                          <div style={{ background: '#0f172a', borderRadius: 8, padding: 12, border: '1px solid #10b981' }}>
                            <Tag color="success" style={{ fontSize: 11, marginBottom: 6 }}>#TN-98</Tag>
                            <div style={{ color: '#94a3b8', fontSize: 13, display: 'flex', alignItems: 'center', gap: 6 }}>
                              <CheckCircleFilled style={{ color: '#10b981' }} /> FastEndpoints Migration v4.2
                            </div>
                            <Text style={{ color: '#64748b', fontSize: 12, display: 'block', marginTop: 4 }}>Completed in 4h</Text>
                          </div>
                        </div>
                      </Col>
                    </Row>
                  </div>
                )}

                {/* Tab 3: AI Accountability Layer (Approve & Undo) */}
                {activeShowcaseTab === 'accountability' && (
                  <div>
                    <Row gutter={[24, 24]} align="middle">
                      <Col xs={24} lg={12}>
                        <div style={{ background: '#1e293b', borderRadius: 12, padding: 22, border: '1px solid #334155' }}>
                          <Text style={{ color: '#94a3b8', fontSize: 13, textTransform: 'uppercase', letterSpacing: 0.5 }}>
                            Bản Ghi Đề Xuất AI (AiActionLog #8492)
                          </Text>
                          <div style={{ marginTop: 12, color: '#f8fafc', fontWeight: 600, fontSize: 16 }}>
                            Đề xuất: Tái cấu trúc &amp; Phân rã Thẻ #TN-104 thành 2 Subtasks
                          </div>
                          <Paragraph style={{ color: '#94a3b8', fontSize: 13, marginTop: 8 }}>
                            Hành động: <code>SplitSubtasks</code> &amp; <code>ReassignTask</code>. AI tự động phát hiện rủi ro quá hạn và đề xuất tách công việc nhưng <strong>chưa bao giờ tự ý sửa DB</strong>.
                          </Paragraph>

                          <div style={{ marginTop: 16, padding: '12px 16px', background: '#0f172a', borderRadius: 8, border: '1px solid #334155' }}>
                            <Flex justify="space-between" align="center">
                              <span style={{ color: '#94a3b8', fontSize: 13 }}>Trạng thái giao dịch:</span>
                              {demoActionStatus === 'pending' && (
                                <Tag color="warning" icon={<ClockCircleOutlined />} style={{ fontWeight: 600 }}>
                                  Pending Approval (Chờ duyệt)
                                </Tag>
                              )}
                              {demoActionStatus === 'approved' && (
                                <Tag color="success" icon={<CheckCircleOutlined />} style={{ fontWeight: 600 }}>
                                  Approved &amp; Applied (Đã áp dụng)
                                </Tag>
                              )}
                              {demoActionStatus === 'undone' && (
                                <Tag color="blue" icon={<UndoOutlined />} style={{ fontWeight: 600 }}>
                                  Undone &amp; Restored (Đã khôi phục)
                                </Tag>
                              )}
                            </Flex>
                          </div>

                          {/* Action Buttons */}
                          <div style={{ marginTop: 20 }}>
                            {demoActionStatus === 'pending' && (
                              <Button
                                type="primary"
                                icon={<CheckOutlined />}
                                onClick={() => setDemoActionStatus('approved')}
                                style={{
                                  background: 'linear-gradient(135deg, #10b981 0%, #059669 100%)',
                                  border: 'none',
                                  fontWeight: 600,
                                  height: 42,
                                  borderRadius: 8,
                                  width: '100%',
                                }}
                              >
                                Phê duyệt &amp; Áp dụng (Commit CAS Transaction)
                              </Button>
                            )}
                            {demoActionStatus === 'approved' && (
                              <Button
                                danger
                                icon={<UndoOutlined />}
                                onClick={() => setDemoActionStatus('undone')}
                                style={{
                                  fontWeight: 600,
                                  height: 42,
                                  borderRadius: 8,
                                  width: '100%',
                                }}
                              >
                                Hoàn tác (Undo 1 chạm — Restore Snapshot)
                              </Button>
                            )}
                            {demoActionStatus === 'undone' && (
                              <Button
                                type="default"
                                icon={<SyncOutlined />}
                                onClick={() => setDemoActionStatus('pending')}
                                style={{
                                  borderColor: '#60a5fa',
                                  color: '#60a5fa',
                                  background: 'transparent',
                                  fontWeight: 600,
                                  height: 42,
                                  borderRadius: 8,
                                  width: '100%',
                                }}
                              >
                                Thử lại kịch bản (Reset Demo)
                              </Button>
                            )}
                          </div>
                        </div>
                      </Col>

                      <Col xs={24} lg={12}>
                        <div style={{ background: '#131d31', borderRadius: 12, padding: 22, border: '1px solid #1e293b' }}>
                          <Text style={{ color: '#a78bfa', fontWeight: 700, fontSize: 14 }}>
                            MINH BẠCH KIỂM TOÁN (AUDIT TRAIL &amp; SNAPSHOT LOG)
                          </Text>
                          <div
                            style={{
                              marginTop: 14,
                              background: '#090d16',
                              borderRadius: 8,
                              padding: 14,
                              fontFamily: "'JetBrains Mono', monospace",
                              fontSize: 12,
                              color: '#cbd5e1',
                              lineHeight: 1.7,
                            }}
                          >
                            <div>{'>'} id: &quot;act_9041a2e&quot;</div>
                            <div>{'>'} action_type: &quot;SplitSubtasks&quot;</div>
                            <div>{'>'} target_entity: &quot;Task #TN-104&quot;</div>
                            <div style={{ color: '#38bdf8' }}>{'>'} before_snapshot: &#123; subtasks: 0, status: &quot;in_progress&quot; &#125;</div>
                            {demoActionStatus === 'approved' && (
                              <div style={{ color: '#34d399' }}>
                                {'>'} after_snapshot: &#123; subtasks: 2, status: &quot;applied&quot; &#125;
                                <br />{'>'} applied_by: &quot;user_manager&quot; at 17:15:20
                              </div>
                            )}
                            {demoActionStatus === 'undone' && (
                              <div style={{ color: '#60a5fa' }}>
                                {'>'} status: &quot;undone&quot;
                                <br />{'>'} restored_from: &quot;before_snapshot&quot; (Atomic 100%)
                              </div>
                            )}
                            {demoActionStatus === 'pending' && (
                              <div style={{ color: '#fbbf24' }}>
                                {'>'} status: &quot;pending&quot; (Chưa ghi vào bảng nghiệp vụ)
                              </div>
                            )}
                          </div>
                          <Text style={{ color: '#64748b', fontSize: 12, marginTop: 10, display: 'block' }}>
                            Mọi thay đổi đều lưu giữ snapshot nguyên tử hai chiều, đảm bảo an toàn tuyệt đối cho dữ liệu doanh nghiệp.
                          </Text>
                        </div>
                      </Col>
                    </Row>
                  </div>
                )}

                {/* Tab 4: Health Score 100 & Proactive Auto-Healing */}
                {activeShowcaseTab === 'health' && (
                  <div>
                    <Row gutter={[24, 24]} align="middle">
                      <Col xs={24} md={8}>
                        <div style={{ background: '#1e293b', borderRadius: 12, padding: 20, textAlign: 'center', border: '1px solid #334155' }}>
                          <Text style={{ color: '#94a3b8', fontSize: 13, textTransform: 'uppercase' }}>
                            Project Health Score
                          </Text>
                          <div style={{ margin: '16px 0' }}>
                            <Progress
                              type="dashboard"
                              percent={92}
                              strokeColor={{ '0%': '#10b981', '100%': '#059669' }}
                              format={() => (
                                <span style={{ color: '#f8fafc', fontSize: 28, fontWeight: 800 }}>
                                  92<span style={{ fontSize: 14, color: '#94a3b8' }}>/100</span>
                                </span>
                              )}
                            />
                          </div>
                          <Tag color="success" style={{ fontWeight: 600 }}>Tối ưu &amp; Ổn định</Tag>
                        </div>
                      </Col>

                      <Col xs={24} md={16}>
                        <div style={{ background: '#1e293b', borderRadius: 12, padding: 20, border: '1px solid #334155' }}>
                          <Text style={{ color: '#e2e8f0', fontWeight: 700, fontSize: 14, display: 'block', marginBottom: 12 }}>
                            5 Thành Phần Trọng Số Chuẩn (Health Weights Breakdown):
                          </Text>
                          <Row gutter={[12, 12]} style={{ fontSize: 12, color: '#94a3b8' }}>
                            <Col span={12}>• Quá hạn (Trọng số 40): <span style={{ color: '#34d399', fontWeight: 600 }}>40/40 (0 thẻ trễ)</span></Col>
                            <Col span={12}>• Rủi ro AtRiskDeadline (Trọng số 20): <span style={{ color: '#fbbf24', fontWeight: 600 }}>17/20 (1 thẻ rủi ro)</span></Col>
                            <Col span={12}>• Tuổi thọ thẻ (Trọng số 15): <span style={{ color: '#34d399', fontWeight: 600 }}>13/15 (TB 2.4 ngày)</span></Col>
                            <Col span={12}>• Lệch tải WIP (Trọng số 15): <span style={{ color: '#fbbf24', fontWeight: 600 }}>12/15 (1 thành viên cận WIP)</span></Col>
                            <Col span={24}>• Đình trệ (Trọng số 10): <span style={{ color: '#34d399', fontWeight: 600 }}>10/10 (Không có thẻ nghẽn &gt; 5 ngày)</span></Col>
                          </Row>

                          <div style={{ marginTop: 16, padding: '12px 14px', background: '#0f172a', borderRadius: 8, borderLeft: '4px solid #6366f1' }}>
                            <Flex justify="space-between" align="center">
                              <div>
                                <Text style={{ color: '#f8fafc', fontWeight: 600, fontSize: 13, display: 'block' }}>
                                  💡 Đề Xuất Tự Phục Hồi (AI Recovery Plan):
                                </Text>
                                <Text style={{ color: '#94a3b8', fontSize: 12 }}>
                                  Phát hiện nguy cơ trễ hạn ở Thẻ #TN-109. AI gợi ý: ReassignTask / SplitSubtasks / ExtendDeadline.
                                </Text>
                              </div>
                              <Button size="small" type="primary" style={{ background: '#4f46e5', borderRadius: 6, fontWeight: 600 }}>
                                Xem Chi Tiết
                              </Button>
                            </Flex>
                          </div>
                        </div>
                      </Col>
                    </Row>
                  </div>
                )}
              </div>
            </div>
          </div>
        </div>

        {/* 4. Phân Khu So Sánh Kiến Trúc Hybrid AI (System 1 vs. System 2) */}
        <div id="hybrid-ai-section" style={{ padding: '80px 24px', background: '#ffffff' }}>
          <div style={{ maxWidth: 1160, margin: '0 auto' }}>
            <div style={{ textAlign: 'center', marginBottom: 50 }}>
              <Tag color="blue" style={{ borderRadius: 9999, fontWeight: 600, padding: '3px 12px' }}>
                HYBRID AI ARCHITECTURE
              </Tag>
              <Title level={2} style={{ fontSize: 32, fontWeight: 800, color: '#0f172a', margin: '12px 0 8px' }}>
                Kiến Trúc Hybrid AI: Tối Ưu Tốc Độ &amp; Chi Phí Token
              </Title>
              <Paragraph style={{ color: '#64748b', fontSize: 16, maxWidth: 760, margin: '0 auto' }}>
                Không phụ thuộc vào một LLM đơn lẻ. Phân tách rõ ràng giữa động cơ ra quyết định phi tạo sinh (System 1) và mô hình ngôn ngữ suy luận sâu (System 2).
              </Paragraph>
            </div>

            <Row gutter={[28, 28]}>
              {/* Khối Trái: System 1 (TypeSafe Jev 1.13) */}
              <Col xs={24} lg={12}>
                <Card
                  style={{
                    height: '100%',
                    borderRadius: 14,
                    border: '1px solid #c7d2fe',
                    background: 'linear-gradient(180deg, #f5f3ff 0%, #ffffff 100%)',
                    boxShadow: '0 10px 25px rgba(99, 102, 241, 0.08)',
                  }}
                  styles={{ body: { padding: 32 } }}
                >
                  <Flex align="center" gap={12} style={{ marginBottom: 16 }}>
                    <div
                      style={{
                        width: 44,
                        height: 44,
                        borderRadius: 10,
                        background: '#4f46e5',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        color: '#ffffff',
                        fontSize: 22,
                      }}
                    >
                      <ThunderboltOutlined />
                    </div>
                    <div>
                      <Title level={4} style={{ margin: 0, color: '#1e1b4b', fontWeight: 800 }}>
                        System 1: TypeSafe Jev 1.13
                      </Title>
                      <Text style={{ color: '#6366f1', fontSize: 13, fontWeight: 600 }}>
                        Non-Generative Decision Engine
                      </Text>
                    </div>
                  </Flex>

                  <Paragraph style={{ color: '#475569', fontSize: 14, lineHeight: 1.6, marginBottom: 20 }}>
                    Động cơ phân loại và phân phối tác vụ chuyên biệt. Không gọi qua endpoint chat thông thường, đảm bảo kết quả định kiểu nghiêm ngặt và độ trễ cực thấp.
                  </Paragraph>

                  <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
                    <div style={{ display: 'flex', gap: 12 }}>
                      <CheckCircleFilled style={{ color: '#4f46e5', fontSize: 16, marginTop: 3 }} />
                      <div>
                        <strong style={{ color: '#0f172a' }}>Endpoint riêng /api/alpha/decisions:</strong>
                        <div style={{ color: '#64748b', fontSize: 13 }}>
                          Tách biệt 100% khỏi luồng chat completions. Quyết định có tính tiền định (deterministic).
                        </div>
                      </div>
                    </div>

                    <div style={{ display: 'flex', gap: 12 }}>
                      <CheckCircleFilled style={{ color: '#4f46e5', fontSize: 16, marginTop: 3 }} />
                      <div>
                        <strong style={{ color: '#0f172a' }}>Đầu ra định kiểu (Choice, Score, Noul):</strong>
                        <div style={{ color: '#64748b', fontSize: 13 }}>
                          Phân loại Domain/WorkType (Choice), thang điểm ưu tiên (Score) và cờ logic bảo mật (Noul).
                        </div>
                      </div>
                    </div>

                    <div style={{ display: 'flex', gap: 12 }}>
                      <CheckCircleFilled style={{ color: '#4f46e5', fontSize: 16, marginTop: 3 }} />
                      <div>
                        <strong style={{ color: '#0f172a' }}>Chống rò rỉ GUID 100%:</strong>
                        <div style={{ color: '#64748b', fontSize: 13 }}>
                          Chỉ gửi nhãn ẩn danh (<code>cand-1</code>, taxonomy keys), không bao giờ gửi UUID, email hay tên thật lên API.
                        </div>
                      </div>
                    </div>

                    <div style={{ display: 'flex', gap: 12 }}>
                      <CheckCircleFilled style={{ color: '#4f46e5', fontSize: 16, marginTop: 3 }} />
                      <div>
                        <strong style={{ color: '#0f172a' }}>Độ trễ &lt; 300ms — Tiết kiệm 90–95% chi phí token:</strong>
                        <div style={{ color: '#64748b', fontSize: 13 }}>
                          Xử lý hàng nghìn yêu cầu phân loại mà không làm tăng vọt hóa đơn token của tổ chức.
                        </div>
                      </div>
                    </div>
                  </div>
                </Card>
              </Col>

              {/* Khối Phải: System 2 (DeepSeek Chat & SSE Streaming) */}
              <Col xs={24} lg={12}>
                <Card
                  style={{
                    height: '100%',
                    borderRadius: 14,
                    border: '1px solid #ddd6fe',
                    background: 'linear-gradient(180deg, #f5f3ff 0%, #ffffff 100%)',
                    boxShadow: '0 10px 25px rgba(124, 58, 237, 0.08)',
                  }}
                  styles={{ body: { padding: 32 } }}
                >
                  <Flex align="center" gap={12} style={{ marginBottom: 16 }}>
                    <div
                      style={{
                        width: 44,
                        height: 44,
                        borderRadius: 10,
                        background: '#7c3aed',
                        display: 'flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        color: '#ffffff',
                        fontSize: 22,
                      }}
                    >
                      <RobotOutlined />
                    </div>
                    <div>
                      <Title level={4} style={{ margin: 0, color: '#2e1065', fontWeight: 800 }}>
                        System 2: DeepSeek Chat
                      </Title>
                      <Text style={{ color: '#7c3aed', fontSize: 13, fontWeight: 600 }}>
                        Language Reasoning &amp; SSE Streaming
                      </Text>
                    </div>
                  </Flex>

                  <Paragraph style={{ color: '#475569', fontSize: 14, lineHeight: 1.6, marginBottom: 20 }}>
                    Mô hình suy luận ngôn ngữ đóng vai trò cố vấn chuyên môn, trợ lý cấp Bảng và cấp Thẻ với khả năng phản hồi trực tiếp dạng stream thời gian thực.
                  </Paragraph>

                  <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
                    <div style={{ display: 'flex', gap: 12 }}>
                      <CheckCircleFilled style={{ color: '#7c3aed', fontSize: 16, marginTop: 3 }} />
                      <div>
                        <strong style={{ color: '#0f172a' }}>Server-Sent Events (SSE) Streaming:</strong>
                        <div style={{ color: '#64748b', fontSize: 13 }}>
                          Giao thức <code>text/event-stream</code> với chuẩn payload: <code>meta</code> → <code>delta*</code> → <code>done | error</code>.
                        </div>
                      </div>
                    </div>

                    <div style={{ display: 'flex', gap: 12 }}>
                      <CheckCircleFilled style={{ color: '#7c3aed', fontSize: 16, marginTop: 3 }} />
                      <div>
                        <strong style={{ color: '#0f172a' }}>Kéo-thả đính kèm ngữ cảnh tối đa 8 thẻ:</strong>
                        <div style={{ color: '#64748b', fontSize: 13 }}>
                          Trợ lý hiểu sâu ngữ cảnh công việc. Tự động sinh <em>Lightweight Board Digest</em> khi không đính kèm thẻ cụ thể.
                        </div>
                      </div>
                    </div>

                    <div style={{ display: 'flex', gap: 12 }}>
                      <CheckCircleFilled style={{ color: '#7c3aed', fontSize: 16, marginTop: 3 }} />
                      <div>
                        <strong style={{ color: '#0f172a' }}>Bộ lọc kép chống rò rỉ định danh:</strong>
                        <div style={{ color: '#64748b', fontSize: 13 }}>
                          <code>AiChatAnswerSanitizer</code> (C# Backend) kết hợp <code>redactIdentifiers.ts</code> (Frontend) lọc sạch thông tin nhạy cảm.
                        </div>
                      </div>
                    </div>

                    <div style={{ display: 'flex', gap: 12 }}>
                      <CheckCircleFilled style={{ color: '#7c3aed', fontSize: 16, marginTop: 3 }} />
                      <div>
                        <strong style={{ color: '#0f172a' }}>Suy luận logic &amp; đề xuất giải pháp:</strong>
                        <div style={{ color: '#64748b', fontSize: 13 }}>
                          Đóng vai trò cố vấn thông minh khi phân rã user story, viết acceptance criteria và rà soát kỹ thuật.
                        </div>
                      </div>
                    </div>
                  </div>
                </Card>
              </Col>
            </Row>
          </div>
        </div>

        {/* 5. Phân Khu Bất Biến Kiến Trúc: AI Accountability Layer */}
        <div id="accountability-agents-section" style={{ padding: '80px 24px', background: '#f8fafc', borderTop: '1px solid #e2e8f0' }}>
          <div style={{ maxWidth: 1160, margin: '0 auto' }}>
            <div style={{ textAlign: 'center', marginBottom: 50 }}>
              <Tag color="green" style={{ borderRadius: 9999, fontWeight: 600, padding: '3px 12px' }}>
                ENTERPRISE INVARIANT
              </Tag>
              <Title level={2} style={{ fontSize: 32, fontWeight: 800, color: '#0f172a', margin: '12px 0 8px' }}>
                Lớp Giải Trình Minh Bạch (AI Accountability Layer)
              </Title>
              <Paragraph style={{ color: '#64748b', fontSize: 16, maxWidth: 760, margin: '0 auto' }}>
                Xóa bỏ hoàn toàn định kiến &quot;hộp đen AI tự tung tự tác&quot;. Mọi đề xuất thay đổi cấu trúc đều phải thông qua phê duyệt tường minh và có thể hoàn tác ngay lập tức.
              </Paragraph>
            </div>

            {/* 4 Bước Kiểm Soát CAS */}
            <Row gutter={[20, 20]} style={{ marginBottom: 40 }}>
              <Col xs={24} sm={12} lg={6}>
                <Card style={{ height: '100%', borderRadius: 12, border: '1px solid #e2e8f0' }} styles={{ body: { padding: 20 } }}>
                  <Tag color="purple" style={{ marginBottom: 10, fontWeight: 700 }}>BƯỚC 1</Tag>
                  <Title level={5} style={{ margin: '0 0 8px', color: '#0f172a' }}>1. AI Sinh Đề Xuất</Title>
                  <Text style={{ color: '#64748b', fontSize: 13, lineHeight: 1.5, display: 'block' }}>
                    AI phân tích bảng hoặc tác vụ, sinh đề xuất dạng payload JSON chuẩn (<code>AiActionService</code>).
                  </Text>
                </Card>
              </Col>
              <Col xs={24} sm={12} lg={6}>
                <Card style={{ height: '100%', borderRadius: 12, border: '1px solid #e2e8f0' }} styles={{ body: { padding: 20 } }}>
                  <Tag color="gold" style={{ marginBottom: 10, fontWeight: 700 }}>BƯỚC 2</Tag>
                  <Title level={5} style={{ margin: '0 0 8px', color: '#0f172a' }}>2. Lưu Pending &amp; Snapshot</Title>
                  <Text style={{ color: '#64748b', fontSize: 13, lineHeight: 1.5, display: 'block' }}>
                    Lưu bản ghi <code>AiActionLog</code> ở trạng thái <code>Pending</code>. Tuyệt đối không tự ý ghi đè DB nghiệp vụ.
                  </Text>
                </Card>
              </Col>
              <Col xs={24} sm={12} lg={6}>
                <Card style={{ height: '100%', borderRadius: 12, border: '1px solid #e2e8f0' }} styles={{ body: { padding: 20 } }}>
                  <Tag color="blue" style={{ marginBottom: 10, fontWeight: 700 }}>BƯỚC 3</Tag>
                  <Title level={5} style={{ margin: '0 0 8px', color: '#0f172a' }}>3. Human-in-the-Loop</Title>
                  <Text style={{ color: '#64748b', fontSize: 13, lineHeight: 1.5, display: 'block' }}>
                    Quản trị viên / Tech Lead xem chi tiết diff thay đổi và bấm <em>Phê duyệt</em> hoặc <em>Từ chối</em>.
                  </Text>
                </Card>
              </Col>
              <Col xs={24} sm={12} lg={6}>
                <Card style={{ height: '100%', borderRadius: 12, border: '1px solid #e2e8f0' }} styles={{ body: { padding: 20 } }}>
                  <Tag color="green" style={{ marginBottom: 10, fontWeight: 700 }}>BƯỚC 4</Tag>
                  <Title level={5} style={{ margin: '0 0 8px', color: '#0f172a' }}>4. Hoàn Tác 1 Chạm</Title>
                  <Text style={{ color: '#64748b', fontSize: 13, lineHeight: 1.5, display: 'block' }}>
                    Hệ thống lưu trữ <code>before_snapshot</code>. Bất kỳ lúc nào người dùng cũng có thể bấm <strong>Undo</strong> để khôi phục 100%.
                  </Text>
                </Card>
              </Col>
            </Row>

            {/* 3 Cam kết cốt lõi */}
            <Card
              style={{
                borderRadius: 14,
                border: '1px solid #bbf7d0',
                background: 'linear-gradient(135deg, #f0fdf4 0%, #ecfdf5 100%)',
              }}
              styles={{ body: { padding: '24px 32px' } }}
            >
              <Row gutter={[24, 20]} align="middle">
                <Col xs={24} md={8}>
                  <Flex align="center" gap={12}>
                    <LockOutlined style={{ fontSize: 24, color: '#16a34a' }} />
                    <div>
                      <strong style={{ color: '#14532d', fontSize: 15, display: 'block' }}>
                        Không bao giờ tự ý ghi đè DB
                      </strong>
                      <span style={{ color: '#166534', fontSize: 13 }}>
                        Zero-unapproved write guarantee.
                      </span>
                    </div>
                  </Flex>
                </Col>
                <Col xs={24} md={8}>
                  <Flex align="center" gap={12}>
                    <AuditOutlined style={{ fontSize: 24, color: '#16a34a' }} />
                    <div>
                      <strong style={{ color: '#14532d', fontSize: 15, display: 'block' }}>
                        Kiểm duyệt tường minh
                      </strong>
                      <span style={{ color: '#166534', fontSize: 13 }}>
                        Dấu vết người duyệt và timestamp rõ ràng.
                      </span>
                    </div>
                  </Flex>
                </Col>
                <Col xs={24} md={8}>
                  <Flex align="center" gap={12}>
                    <UndoOutlined style={{ fontSize: 24, color: '#16a34a' }} />
                    <div>
                      <strong style={{ color: '#14532d', fontSize: 15, display: 'block' }}>
                        Hoàn tác nguyên tử (Undo)
                      </strong>
                      <span style={{ color: '#166534', fontSize: 13 }}>
                        Khôi phục nguyên trạng snapshot tức thì.
                      </span>
                    </div>
                  </Flex>
                </Col>
              </Row>
            </Card>
          </div>
        </div>

        {/* 6. Phân Khu Đội Ngũ AI Agent Chuyên Gia & Kho Tri Thức Ngữ Nghĩa (pgvector RAG) */}
        <div style={{ padding: '80px 24px', background: '#ffffff' }}>
          <div style={{ maxWidth: 1160, margin: '0 auto' }}>
            <div style={{ textAlign: 'center', marginBottom: 50 }}>
              <Tag color="cyan" style={{ borderRadius: 9999, fontWeight: 600, padding: '3px 12px' }}>
                MULTI-AGENT &amp; SEMANTIC RAG
              </Tag>
              <Title level={2} style={{ fontSize: 32, fontWeight: 800, color: '#0f172a', margin: '12px 0 8px' }}>
                Đội Ngũ AI Agent Chuyên Gia &amp; Kho Tri Thức Doanh Nghiệp
              </Title>
              <Paragraph style={{ color: '#64748b', fontSize: 16, maxWidth: 760, margin: '0 auto' }}>
                Mỗi AI Agent đóng vai trò như một đồng nghiệp ảo (<code>member_type = &apos;ai_agent&apos;</code>) hoạt động dưới các guardrail chặt chẽ và tra cứu chuẩn kiến thức nội bộ qua pgvector.
              </Paragraph>
            </div>

            {/* 4 Persona Cards */}
            <Row gutter={[20, 20]} style={{ marginBottom: 40 }}>
              <Col xs={24} sm={12} lg={6}>
                <Card
                  hoverable
                  style={{ height: '100%', borderRadius: 12, border: '1px solid #e2e8f0' }}
                  styles={{ body: { padding: 22 } }}
                >
                  <Tag color="blue" style={{ marginBottom: 12, fontFamily: "'JetBrains Mono', monospace", fontWeight: 700 }}>
                    @general
                  </Tag>
                  <Title level={5} style={{ margin: '0 0 8px', color: '#0f172a' }}>
                    Trợ Lý Điều Phối Chung
                  </Title>
                  <Text style={{ color: '#64748b', fontSize: 13, lineHeight: 1.5, display: 'block' }}>
                    Tóm tắt tiến độ Workspace, hỗ trợ tạo nhanh danh sách thẻ công việc và lập báo cáo tổng hợp nhanh.
                  </Text>
                </Card>
              </Col>

              <Col xs={24} sm={12} lg={6}>
                <Card
                  hoverable
                  style={{ height: '100%', borderRadius: 12, border: '1px solid #e2e8f0' }}
                  styles={{ body: { padding: 22 } }}
                >
                  <Tag color="orange" style={{ marginBottom: 12, fontFamily: "'JetBrains Mono', monospace", fontWeight: 700 }}>
                    @scrum_master
                  </Tag>
                  <Title level={5} style={{ margin: '0 0 8px', color: '#0f172a' }}>
                    Quản Trị Agile &amp; Sprint
                  </Title>
                  <Text style={{ color: '#64748b', fontSize: 13, lineHeight: 1.5, display: 'block' }}>
                    Theo dõi biểu đồ Burndown, phát hiện tắc nghẽn WIP limits, đề xuất phân rã story và tối ưu vận tốc đội ngũ.
                  </Text>
                </Card>
              </Col>

              <Col xs={24} sm={12} lg={6}>
                <Card
                  hoverable
                  style={{ height: '100%', borderRadius: 12, border: '1px solid #e2e8f0' }}
                  styles={{ body: { padding: 22 } }}
                >
                  <Tag color="purple" style={{ marginBottom: 12, fontFamily: "'JetBrains Mono', monospace", fontWeight: 700 }}>
                    @tech_lead
                  </Tag>
                  <Title level={5} style={{ margin: '0 0 8px', color: '#0f172a' }}>
                    Trưởng Nhóm Kỹ Thuật
                  </Title>
                  <Text style={{ color: '#64748b', fontSize: 13, lineHeight: 1.5, display: 'block' }}>
                    Đánh giá kiến trúc kỹ thuật, rà soát mức độ phức tạp, gợi ý phân bổ thành viên theo kỹ năng chuyên môn.
                  </Text>
                </Card>
              </Col>

              <Col xs={24} sm={12} lg={6}>
                <Card
                  hoverable
                  style={{ height: '100%', borderRadius: 12, border: '1px solid #e2e8f0' }}
                  styles={{ body: { padding: 22 } }}
                >
                  <Tag color="red" style={{ marginBottom: 12, fontFamily: "'JetBrains Mono', monospace", fontWeight: 700 }}>
                    @qa_security
                  </Tag>
                  <Title level={5} style={{ margin: '0 0 8px', color: '#0f172a' }}>
                    Kiểm Thử &amp; An Toàn
                  </Title>
                  <Text style={{ color: '#64748b', fontSize: 13, lineHeight: 1.5, display: 'block' }}>
                    Kiểm định tiêu chí hoàn thành (Acceptance Criteria), rà soát lỗ hổng bảo mật và kiểm tra chuẩn OWASP.
                  </Text>
                </Card>
              </Col>
            </Row>

            {/* Semantic Knowledge Base & Reports Banner */}
            <Row gutter={[24, 24]}>
              <Col xs={24} md={12}>
                <Card
                  style={{ height: '100%', borderRadius: 12, border: '1px solid #e2e8f0', background: '#f8fafc' }}
                  styles={{ body: { padding: 24 } }}
                >
                  <Flex gap={14}>
                    <DatabaseOutlined style={{ fontSize: 28, color: '#4f46e5', marginTop: 4 }} />
                    <div>
                      <Title level={5} style={{ margin: '0 0 6px', color: '#0f172a' }}>
                        Kho Tri Thức Ngữ Nghĩa (pgvector HNSW RAG)
                      </Title>
                      <Paragraph style={{ color: '#64748b', fontSize: 13, lineHeight: 1.6, margin: 0 }}>
                        Lưu trữ tài liệu dự án trong bảng <code>workspace_knowledge_chunks</code> và lập chỉ mục HNSW vector cosine trên PostgreSQL 16. AI trả lời chính xác dựa trên quy định nội bộ của đội ngũ.
                      </Paragraph>
                    </div>
                  </Flex>
                </Card>
              </Col>

              <Col xs={24} md={12}>
                <Card
                  style={{ height: '100%', borderRadius: 12, border: '1px solid #e2e8f0', background: '#f8fafc' }}
                  styles={{ body: { padding: 24 } }}
                >
                  <Flex gap={14}>
                    <FileTextOutlined style={{ fontSize: 28, color: '#10b981', marginTop: 4 }} />
                    <div>
                      <Title level={5} style={{ margin: '0 0 6px', color: '#0f172a' }}>
                        Báo Cáo Streaming &amp; Daily Digest
                      </Title>
                      <Paragraph style={{ color: '#64748b', fontSize: 13, lineHeight: 1.6, margin: 0 }}>
                        Xuất báo cáo PDF (<code>QuestPDF</code>) &amp; Excel (<code>ClosedXML</code>) trực tiếp từ RAM stream không ghi file tạm. Tự động gửi Daily Email Digest tóm tắt tiến độ mỗi sáng qua Resend.
                      </Paragraph>
                    </div>
                  </Flex>
                </Card>
              </Col>
            </Row>
          </div>
        </div>

        {/* 7. Phân Khu Kiến Trúc Modular Monolith (.NET 10) & Tầm Nhìn Mở Rộng Đa Nền Tảng */}
        <div id="architecture-ecosystem-section" style={{ padding: '80px 24px', background: '#f8fafc', borderTop: '1px solid #e2e8f0', color: '#0f172a' }}>
          <div style={{ maxWidth: 1160, margin: '0 auto' }}>
            <div style={{ textAlign: 'center', marginBottom: 50 }}>
              <Tag
                color="blue"
                style={{
                  borderRadius: 9999,
                  fontWeight: 600,
                  padding: '3px 12px',
                  fontFamily: "'JetBrains Mono', monospace",
                }}
              >
                ENGINEERING REALITY &amp; ROADMAP
              </Tag>
              <Title level={2} style={{ fontSize: 32, fontWeight: 800, color: '#0f172a', margin: '12px 0 8px' }}>
                Kiến Trúc .NET 10 Modular Monolith &amp; Tầm Nhìn Đa Nền Tảng
              </Title>
              <Paragraph style={{ color: '#64748b', fontSize: 16, maxWidth: 760, margin: '0 auto' }}>
                Hợp đồng kiến trúc thật đã hiện thực 100% trong giải pháp <code>TeamNexus.sln</code> cùng định hướng mở rộng hệ sinh thái webhook và ứng dụng di động Flutter.
              </Paragraph>
            </div>

            <Row gutter={[28, 28]}>
              {/* Khối Cấu Trúc Giải Pháp Thực Tế (Light Elegant Bento) */}
              <Col xs={24} lg={12}>
                <div
                  style={{
                    background: '#ffffff',
                    borderRadius: 14,
                    border: '1px solid #e2e8f0',
                    boxShadow: '0 4px 20px rgba(15, 23, 42, 0.04)',
                    padding: 24,
                    height: '100%',
                  }}
                >
                  <Flex justify="space-between" align="center" style={{ marginBottom: 16 }}>
                    <Flex align="center" gap={8}>
                      <CodeOutlined style={{ color: '#4f46e5', fontSize: 18 }} />
                      <Text style={{ color: '#0f172a', fontWeight: 700, fontSize: 15 }}>
                        Cấu Trúc Giải Pháp Thực Tế (.NET 10)
                      </Text>
                    </Flex>
                    <Tag color="purple" style={{ fontFamily: "'JetBrains Mono', monospace" }}>
                      TeamNexus.sln
                    </Tag>
                  </Flex>

                  <div
                    style={{
                      background: '#f8fafc',
                      borderRadius: 10,
                      padding: '16px 18px',
                      fontFamily: "'JetBrains Mono', monospace",
                      fontSize: 12.5,
                      color: '#475569',
                      lineHeight: 1.8,
                      border: '1px solid #e2e8f0',
                    }}
                  >
                    <div style={{ color: '#4f46e5', fontWeight: 700 }}>TeamNexus.sln</div>
                    <div>├── 📁 <strong style={{ color: '#0f172a' }}>TeamNexus.Api</strong> (FastEndpoints, Middlewares, SignalR Hubs)</div>
                    <div>├── 📁 <strong style={{ color: '#0f172a' }}>TeamNexus.Persistence</strong> (PostgreSQL 16, pgvector, 14 Migrations)</div>
                    <div>├── 📁 <strong style={{ color: '#0f172a' }}>Modules.Auth</strong> (JWT, Google/GitHub OAuth, RBAC)</div>
                    <div>├── 📁 <strong style={{ color: '#0f172a' }}>Modules.Board</strong> (Kanban Columns, Tasks, WIP Limits, CAS)</div>
                    <div>├── 📁 <strong style={{ color: '#0f172a' }}>Modules.Ai</strong> (Jev System 1, DeepSeek System 2, AiActionService)</div>
                    <div>├── 📁 <strong style={{ color: '#0f172a' }}>Modules.Reporting</strong> (Burndown, Velocity, QuestPDF, ClosedXML)</div>
                    <div>└── 📁 <strong style={{ color: '#0f172a' }}>TeamNexus.Shared</strong> (Result Pattern, Domain Primitives, Errors)</div>
                  </div>

                  <div style={{ marginTop: 20 }}>
                    <Row gutter={[16, 12]}>
                      <Col span={12}>
                        <div style={{ padding: '12px 14px', background: '#f8fafc', borderRadius: 8, border: '1px solid #e2e8f0' }}>
                          <Text style={{ color: '#64748b', fontSize: 11, display: 'block' }}>Backend Tests (xUnit)</Text>
                          <Text style={{ color: '#10b981', fontSize: 18, fontWeight: 700 }}>912 Tests PASS</Text>
                        </div>
                      </Col>
                      <Col span={12}>
                        <div style={{ padding: '12px 14px', background: '#f8fafc', borderRadius: 8, border: '1px solid #e2e8f0' }}>
                          <Text style={{ color: '#64748b', fontSize: 11, display: 'block' }}>Frontend Tests (Vitest)</Text>
                          <Text style={{ color: '#10b981', fontSize: 18, fontWeight: 700 }}>≥ 738 Tests PASS</Text>
                        </div>
                      </Col>
                    </Row>
                  </div>
                </div>
              </Col>

              {/* Khối Mở Rộng Hệ Sinh Thái & Đa Nền Tảng */}
              <Col xs={24} lg={12}>
                <div style={{ display: 'flex', flexDirection: 'column', gap: 20, height: '100%' }}>
                  {/* Hướng mở rộng 1: Webhook Ecosystem */}
                  <div
                    style={{
                      background: '#ffffff',
                      borderRadius: 14,
                      border: '1px solid #e2e8f0',
                      boxShadow: '0 4px 20px rgba(15, 23, 42, 0.04)',
                      padding: 22,
                      flex: 1,
                    }}
                  >
                    <Flex align="center" gap={10} style={{ marginBottom: 12 }}>
                      <div
                        style={{
                          width: 36,
                          height: 36,
                          borderRadius: 8,
                          background: '#fffbeb',
                          border: '1px solid #fde68a',
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                        }}
                      >
                        <BranchesOutlined style={{ color: '#d97706', fontSize: 18 }} />
                      </div>
                      <Title level={5} style={{ margin: 0, color: '#0f172a', fontWeight: 700 }}>
                        Hướng Mở Rộng 1: Hệ Sinh Thái Mở (Webhooks)
                      </Title>
                    </Flex>
                    <Paragraph style={{ color: '#475569', fontSize: 13, lineHeight: 1.6, margin: 0 }}>
                      Đồng bộ tự động khi GitHub PR Merged → chuyển trạng thái thẻ sang cột <code style={{ background: '#f1f5f9', padding: '2px 6px', borderRadius: 4, color: '#0f172a' }}>Done</code> với xác thực chữ ký số <code style={{ background: '#f1f5f9', padding: '2px 6px', borderRadius: 4, color: '#0f172a' }}>HMAC-SHA256</code> và mã hóa PAT an toàn. Đẩy cảnh báo khẩn cấp từ AI Observer sang Slack/Discord qua <code style={{ background: '#f1f5f9', padding: '2px 6px', borderRadius: 4, color: '#0f172a' }}>IExternalAlertSink</code> với cơ chế phục hồi suy giảm nhẹ nhàng (Graceful Degradation).
                    </Paragraph>
                  </div>

                  {/* Hướng mở rộng 2: Flutter Mobile */}
                  <div
                    style={{
                      background: '#ffffff',
                      borderRadius: 14,
                      border: '1px solid #e2e8f0',
                      boxShadow: '0 4px 20px rgba(15, 23, 42, 0.04)',
                      padding: 22,
                      flex: 1,
                    }}
                  >
                    <Flex align="center" gap={10} style={{ marginBottom: 12 }}>
                      <div
                        style={{
                          width: 36,
                          height: 36,
                          borderRadius: 8,
                          background: '#f5f3ff',
                          border: '1px solid #ddd6fe',
                          display: 'flex',
                          alignItems: 'center',
                          justifyContent: 'center',
                        }}
                      >
                        <MobileOutlined style={{ color: '#7c3aed', fontSize: 18 }} />
                      </div>
                      <Title level={5} style={{ margin: 0, color: '#0f172a', fontWeight: 700 }}>
                        Hướng Mở Rộng 2: Ứng Dụng Di Động Đa Nền Tảng (Flutter)
                      </Title>
                    </Flex>
                    <Paragraph style={{ color: '#475569', fontSize: 13, lineHeight: 1.6, margin: 0 }}>
                      Trải nghiệm iOS &amp; Android viết bằng Flutter, tái sử dụng 100% hợp đồng RESTful API và SignalR <code style={{ background: '#f1f5f9', padding: '2px 6px', borderRadius: 4, color: '#0f172a' }}>BoardHub</code>. Lưu trữ khóa an toàn với <code style={{ background: '#f1f5f9', padding: '2px 6px', borderRadius: 4, color: '#0f172a' }}>flutter_secure_storage</code>, hỗ trợ kéo-thả Kanban cảm ứng và phê duyệt đề xuất AI mọi lúc mọi nơi.
                    </Paragraph>
                  </div>
                </div>
              </Col>
            </Row>
          </div>
        </div>

        {/* 8. Conversion Banner */}
        <div style={{ padding: '80px 24px', background: '#ffffff', textAlign: 'center' }}>
          <div style={{ maxWidth: 880, margin: '0 auto' }}>
            <Card
              style={{
                borderRadius: 20,
                border: 'none',
                background: 'linear-gradient(135deg, #1e1b4b 0%, #312e81 50%, #4c1d95 100%)',
                boxShadow: '0 20px 50px rgba(79, 70, 229, 0.25)',
                color: '#ffffff',
              }}
              styles={{ body: { padding: '50px 36px' } }}
            >
              <NexusLogo size={56} style={{ marginBottom: 16 }} />
              <Title level={2} style={{ color: '#ffffff', fontSize: 32, fontWeight: 800, margin: '0 0 16px' }}>
                Sẵn Sàng Nâng Tầm Năng Suất Đội Ngũ Cùng TeamNexus?
              </Title>
              <Paragraph style={{ color: '#c7d2fe', fontSize: 16, maxWidth: 620, margin: '0 auto 32px' }}>
                Tham gia trải nghiệm nền tảng điều phối không gian làm việc thông minh kết hợp Hybrid AI và Lớp Giải Trình Minh Bạch ngay hôm nay.
              </Paragraph>
              <Flex justify="center" gap={16} wrap="wrap">
                <Button
                  type="primary"
                  size="large"
                  icon={<GitHubIcon />}
                  onClick={() => loginWithProvider('github')}
                  style={{
                    height: 50,
                    padding: '0 28px',
                    fontSize: 15,
                    fontWeight: 600,
                    borderRadius: 10,
                    background: '#ffffff',
                    color: '#0f172a',
                    border: 'none',
                    boxShadow: '0 4px 14px rgba(0, 0, 0, 0.2)',
                  }}
                >
                  Bắt đầu ngay với GitHub
                </Button>
                <Button
                  size="large"
                  icon={<GoogleIcon />}
                  onClick={() => loginWithProvider('google')}
                  style={{
                    height: 50,
                    padding: '0 28px',
                    fontSize: 15,
                    fontWeight: 600,
                    borderRadius: 10,
                    background: 'rgba(255, 255, 255, 0.12)',
                    borderColor: 'rgba(255, 255, 255, 0.25)',
                    color: '#ffffff',
                  }}
                >
                  Tiếp tục với Google
                </Button>
              </Flex>
            </Card>
          </div>
        </div>
      </Content>

      {/* Luminous Footer */}
      <Footer
        style={{
          background: '#ffffff',
          borderTop: '1px solid #e2e8f0',
          padding: '40px 32px 30px',
          color: '#64748b',
        }}
      >
        <div style={{ maxWidth: 1160, margin: '0 auto' }}>
          <Row gutter={[32, 24]} justify="space-between" align="middle">
            <Col xs={24} md={10}>
              <Flex align="center" gap={10} style={{ marginBottom: 10 }}>
                <NexusLogo size={28} />
                <Text style={{ color: '#0f172a', fontWeight: 800, fontSize: 18 }}>TeamNexus</Text>
                <Tag
                  style={{
                    borderRadius: 9999,
                    fontSize: 11,
                    fontWeight: 600,
                    color: '#7c3aed',
                    backgroundColor: '#f5f3ff',
                    borderColor: '#ddd6fe',
                    fontFamily: "'JetBrains Mono', monospace",
                  }}
                >
                  v4.2 Enterprise
                </Tag>
              </Flex>
              <Text style={{ color: '#64748b', fontSize: 13, display: 'block' }}>
                Nền tảng Điều phối Không gian Làm việc Thông minh với Hybrid AI &amp; Lớp Giải trình Minh bạch.
              </Text>
            </Col>

            <Col xs={24} md={14}>
              <Flex justify="flex-end" gap={20} wrap="wrap">
                <Button type="link" onClick={() => scrollToSection('interactive-showcase')} style={{ color: '#475569', padding: 0, fontWeight: 500 }}>
                  Trải nghiệm
                </Button>
                <Button type="link" onClick={() => scrollToSection('hybrid-ai-section')} style={{ color: '#475569', padding: 0, fontWeight: 500 }}>
                  Hybrid AI
                </Button>
                <Button type="link" onClick={() => scrollToSection('accountability-agents-section')} style={{ color: '#475569', padding: 0, fontWeight: 500 }}>
                  Lớp giải trình
                </Button>
                <Button type="link" onClick={() => scrollToSection('architecture-ecosystem-section')} style={{ color: '#475569', padding: 0, fontWeight: 500 }}>
                  Kiến trúc .NET 10
                </Button>
                <Button type="link" onClick={() => navigate('/login')} style={{ color: '#4f46e5', padding: 0, fontWeight: 600 }}>
                  Đăng nhập →
                </Button>
              </Flex>
            </Col>
          </Row>

          <div
            style={{
              marginTop: 30,
              paddingTop: 20,
              borderTop: '1px solid #f1f5f9',
              textAlign: 'center',
              fontSize: 12,
              color: '#94a3b8',
            }}
          >
            © 2026 TeamNexus. All rights reserved.
          </div>
        </div>
      </Footer>
    </Layout>
  )
}
