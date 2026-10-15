import React, { useCallback, useEffect, useState } from 'react'
import {
  ArrowLeftOutlined,
  BarChartOutlined,
  DashboardOutlined,
  DatabaseOutlined,
  DeleteOutlined,
  EditOutlined,
  FolderOpenOutlined,
  HistoryOutlined,
  PlusOutlined,
  ProjectOutlined,
  ReloadOutlined,
  SearchOutlined,
  SettingOutlined,
  TeamOutlined,
  RobotOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Col,
  Empty,
  Flex,
  Layout,
  Popconfirm,
  Row,
  Space,
  Spin,
  Tooltip,
  Typography,
  message,
} from 'antd'
import dayjs from 'dayjs'
import { useNavigate, useParams } from 'react-router-dom'
import { useWorkspaceRole } from '../../../shared/hooks/useWorkspaceRole'
import { AppHeader } from '../../../shared/components/AppHeader'
import { BoardModal } from '../components/BoardModal'
import { BoardTemplateModal } from '../../ai'
import { AiActionHistoryDrawer } from '../../ai/components/AiActionHistoryDrawer'
import { boardApi } from '../services/boardApi'
import type { BoardResponse, CreateBoardRequest, UpdateBoardRequest } from '../types/board.types'

const { Content } = Layout

export const BoardListPage: React.FC = () => {
  const { workspaceId } = useParams<{ workspaceId: string }>()
  const navigate = useNavigate()
  const { isManagerOrAdmin } = useWorkspaceRole(workspaceId)

  const [boards, setBoards] = useState<BoardResponse[]>([])
  const [loading, setLoading] = useState(true)
  const [modalOpen, setModalOpen] = useState(false)
  const [editingBoard, setEditingBoard] = useState<BoardResponse | null>(null)
  const [aiTemplateModalOpen, setAiTemplateModalOpen] = useState(false)

  /**
   * Workspace-wide AI history (Phase 14.5 A1.1). Separate from the board's own drawer on purpose: a log
   * that creates a board is scoped to the WORKSPACE, so without this the only way to approve or undo a
   * board-template proposal was to open the board it had already created.
   */
  const [aiHistoryOpen, setAiHistoryOpen] = useState(false)

  const fetchBoards = useCallback(async () => {
    if (!workspaceId) return
    setLoading(true)
    try {
      const data = await boardApi.getBoards(workspaceId)
      setBoards(data)
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Không thể tải danh sách bảng'
      message.error(errorMsg)
    } finally {
      setLoading(false)
    }
  }, [workspaceId])

  useEffect(() => {
    if (!workspaceId) return
    let ignore = false

    boardApi
      .getBoards(workspaceId)
      .then((data) => {
        if (!ignore) {
          setBoards(data)
          setLoading(false)
        }
      })
      .catch((err: unknown) => {
        if (!ignore) {
          const errorMsg =
            (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
            'Không thể tải danh sách bảng'
          message.error(errorMsg)
          setLoading(false)
        }
      })

    return () => {
      ignore = true
    }
  }, [workspaceId])

  const handleCreateOrUpdateBoard = async (data: CreateBoardRequest | UpdateBoardRequest) => {
    if (!workspaceId) return
    try {
      if (editingBoard) {
        const updated = await boardApi.updateBoard(workspaceId, editingBoard.id, data)
        setBoards((prev) => prev.map((b) => (b.id === updated.id ? updated : b)))
        message.success('Cập nhật bảng thành công')
      } else {
        const created = await boardApi.createBoard(workspaceId, data)
        setBoards((prev) => [created, ...prev])
        message.success(`Đã tạo bảng "${created.name}"`)
      }
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Thao tác thất bại'
      message.error(errorMsg)
      throw err
    }
  }

  const handleDeleteBoard = async (boardId: string) => {
    if (!workspaceId) return
    try {
      await boardApi.deleteBoard(workspaceId, boardId)
      setBoards((prev) => prev.filter((b) => b.id !== boardId))
      message.success('Đã xoá bảng thành công')
    } catch (err: unknown) {
      const errorMsg =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error ??
        'Xoá bảng thất bại'
      message.error(errorMsg)
    }
  }

  return (
    <Layout style={{ minHeight: '100vh', background: '#f8fafc' }}>
      {/* Top Header */}
      <AppHeader workspaceId={workspaceId} />

      <Content style={{ padding: '32px 24px', maxWidth: 1100, margin: '0 auto', width: '100%' }}>
        <Flex vertical gap="large">
          {/* Header controls */}
          <Flex justify="space-between" align="center" wrap="wrap" gap={12}>
            <Flex align="center" gap={8}>
              <Button type="text" icon={<ArrowLeftOutlined />} onClick={() => navigate('/')} />
              <div>
                <Typography.Title level={3} style={{ margin: 0, color: '#0f172a' }}>
                  Danh Sách Bảng Kanban
                </Typography.Title>
                <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                  Workspace: {workspaceId}
                </Typography.Text>
              </div>
            </Flex>

            <Space>
              <Button
                icon={<DashboardOutlined />}
                style={{ borderRadius: 8, borderColor: '#cbd5e1', color: '#475569' }}
                onClick={() => navigate(`/workspaces/${workspaceId}/dashboard`)}
                data-testid="nav-dashboard-btn"
              >
                Tổng quan
              </Button>
              <Button
                icon={<SearchOutlined />}
                style={{ borderRadius: 8, borderColor: '#cbd5e1', color: '#475569' }}
                onClick={() => navigate(`/workspaces/${workspaceId}/search`)}
                data-testid="nav-search-btn"
              >
                Tìm kiếm
              </Button>
              <Tooltip title="Làm mới">
                <Button icon={<ReloadOutlined />} onClick={fetchBoards} />
              </Tooltip>
              <Button
                icon={<TeamOutlined />}
                style={{ borderRadius: 8, borderColor: '#cbd5e1', color: '#475569' }}
                onClick={() => navigate(`/workspaces/${workspaceId}/members`)}
                data-testid="nav-members-toolbar-btn"
              >
                Thành viên
              </Button>
              {/* Phase 14.7 §B: the knowledge base is workspace-scoped and Member+ to read, so it sits next
                  to "Thành viên" — the other page reachable without Manager rights. */}
              <Button
                icon={<DatabaseOutlined />}
                style={{ borderRadius: 8, borderColor: '#cbd5e1', color: '#475569' }}
                onClick={() => navigate(`/workspaces/${workspaceId}/knowledge`)}
                data-testid="nav-knowledge-toolbar-btn"
              >
                Kho tri thức
              </Button>
              <Button
                icon={<BarChartOutlined />}
                style={{ borderRadius: 8, borderColor: '#cbd5e1', color: '#475569' }}
                onClick={() => navigate(`/workspaces/${workspaceId}/reports`)}
              >
                Báo cáo
              </Button>
              {isManagerOrAdmin && (
                <>
                  <Button
                    icon={<HistoryOutlined />}
                    style={{ borderRadius: 8, borderColor: '#cbd5e1', color: '#475569' }}
                    onClick={() => navigate(`/workspaces/${workspaceId}/activity`)}
                  >
                    Hoạt động
                  </Button>
                  <Button
                    icon={<SettingOutlined />}
                    style={{ borderRadius: 8, borderColor: '#cbd5e1', color: '#475569' }}
                    onClick={() => navigate(`/workspaces/${workspaceId}/settings`)}
                  >
                    Cài đặt
                  </Button>
                  <Button
                    icon={<RobotOutlined />}
                    data-testid="board-template-btn"
                    style={{ borderRadius: 8, borderColor: '#8b5cf6', color: '#7c3aed' }}
                    onClick={() => setAiTemplateModalOpen(true)}
                  >
                    Tạo board bằng AI
                  </Button>
                  <Button
                    icon={<HistoryOutlined />}
                    data-testid="workspace-ai-history-btn"
                    style={{ borderRadius: 8, borderColor: '#cbd5e1', color: '#475569' }}
                    onClick={() => setAiHistoryOpen(true)}
                  >
                    Lịch sử AI
                  </Button>
                </>
              )}
              <Button
                type="primary"
                icon={<PlusOutlined />}
                style={{ backgroundColor: '#6366f1', borderRadius: 8 }}
                onClick={() => {
                  setEditingBoard(null)
                  setModalOpen(true)
                }}
              >
                Tạo Bảng Mới
              </Button>
            </Space>
          </Flex>

          {/* Boards Grid */}
          {loading ? (
            <Flex align="center" justify="center" style={{ height: 300 }}>
              <Spin size="large" tip="Đang tải danh sách bảng..." />
            </Flex>
          ) : boards.length === 0 ? (
            <Card style={{ borderRadius: 12, padding: 32, textAlign: 'center' }}>
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="Workspace chưa có bảng Kanban nào"
              >
                <Button
                  type="primary"
                  icon={<PlusOutlined />}
                  style={{ backgroundColor: '#6366f1', marginTop: 12 }}
                  onClick={() => {
                    setEditingBoard(null)
                    setModalOpen(true)
                  }}
                >
                  Tạo bảng đầu tiên
                </Button>
              </Empty>
            </Card>
          ) : (
            <Row gutter={[16, 16]}>
              {boards.map((board) => (
                <Col xs={24} sm={12} lg={8} key={board.id}>
                  <Card
                    hoverable
                    style={{
                      borderRadius: 12,
                      border: '1px solid #e2e8f0',
                      boxShadow: '0 2px 8px rgba(0,0,0,0.04)',
                      height: '100%',
                      display: 'flex',
                      flexDirection: 'column',
                    }}
                    bodyStyle={{ flex: 1, display: 'flex', flexDirection: 'column' }}
                    onClick={() => navigate(`/workspaces/${workspaceId}/boards/${board.id}`)}
                  >
                    <Flex justify="space-between" align="flex-start" style={{ marginBottom: 12 }}>
                      <Flex align="center" gap={10}>
                        <div
                          style={{
                            width: 36,
                            height: 36,
                            borderRadius: 8,
                            backgroundColor: '#e0e7ff',
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'center',
                            color: '#6366f1',
                            fontSize: 18,
                          }}
                        >
                          <ProjectOutlined />
                        </div>
                        <div>
                          <Typography.Text strong style={{ fontSize: 16, color: '#0f172a' }}>
                            {board.name}
                          </Typography.Text>
                          <Typography.Text type="secondary" style={{ display: 'block', fontSize: 11 }}>
                            Tạo: {dayjs(board.createdAt).format('DD/MM/YYYY')}
                          </Typography.Text>
                        </div>
                      </Flex>

                      <Space size={4} onClick={(e) => e.stopPropagation()}>
                        <Button
                          type="text"
                          size="small"
                          icon={<EditOutlined />}
                          onClick={() => {
                            setEditingBoard(board)
                            setModalOpen(true)
                          }}
                        />
                        <Popconfirm
                          title="Xoá bảng này?"
                          description="Tất cả các cột và thẻ trong bảng sẽ bị xoá."
                          okText="Xoá"
                          cancelText="Huỷ"
                          okButtonProps={{ danger: true }}
                          onConfirm={() => handleDeleteBoard(board.id)}
                        >
                          <Button
                            type="text"
                            size="small"
                            danger
                            icon={<DeleteOutlined />}
                          />
                        </Popconfirm>
                      </Space>
                    </Flex>

                    <Typography.Paragraph
                      type="secondary"
                      ellipsis={{ rows: 2 }}
                      style={{ fontSize: 13, flex: 1, margin: '8px 0 16px 0' }}
                    >
                      {board.description || 'Không có mô tả'}
                    </Typography.Paragraph>

                    <Flex justify="flex-end" align="center" style={{ borderTop: '1px solid #f1f5f9', paddingTop: 10 }}>
                      <Button
                        type="link"
                        size="small"
                        icon={<FolderOpenOutlined />}
                        style={{ color: '#6366f1', padding: 0 }}
                      >
                        Mở bảng Kanban →
                      </Button>
                    </Flex>
                  </Card>
                </Col>
              ))}
            </Row>
          )}
        </Flex>
      </Content>

      <BoardModal
        open={modalOpen}
        board={editingBoard}
        onClose={() => {
          setModalOpen(false)
          setEditingBoard(null)
        }}
        onSubmit={handleCreateOrUpdateBoard}
      />

      <BoardTemplateModal
        open={aiTemplateModalOpen}
        onClose={() => setAiTemplateModalOpen(false)}
        workspaceId={workspaceId ?? ''}
        onCreated={fetchBoards}
      />

      {/* Workspace-scoped history: an approved board-template action CREATES a board, so the list has to be
          re-fetched after a decision — otherwise the new board only appears on the next manual reload. */}
      {isManagerOrAdmin && (
        <AiActionHistoryDrawer
          open={aiHistoryOpen}
          onClose={() => setAiHistoryOpen(false)}
          workspaceId={workspaceId ?? ''}
          onBoardChanged={fetchBoards}
        />
      )}
    </Layout>
  )
}
