import React, { useState } from 'react'
import {
  Alert,
  Button,
  Card,
  Col,
  Empty,
  Flex,
  Layout,
  Popconfirm,
  Row,
  Space,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from 'antd'
import type { ColumnsType } from 'antd/es/table'
import {
  ArrowLeftOutlined,
  CheckCircleOutlined,
  CloseCircleOutlined,
  DatabaseOutlined,
  DeleteOutlined,
  LoadingOutlined,
  PlusOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import dayjs from 'dayjs'
import { useNavigate, useParams } from 'react-router-dom'
import { AppHeader } from '../../../shared/components/AppHeader'
import { useWorkspaceRole } from '../../../shared/hooks/useWorkspaceRole'
import { useKnowledgeDocuments } from '../hooks/useKnowledgeDocuments'
import { KnowledgeDocumentForm } from './KnowledgeDocumentForm'
import {
  KNOWLEDGE_STATUS_COLORS,
  KNOWLEDGE_STATUS_LABELS,
} from '../types/knowledge.types'
import type {
  CreateKnowledgeDocumentRequest,
  KnowledgeDocument,
  KnowledgeDocumentStatus,
} from '../types/knowledge.types'

const { Content } = Layout
const { Text } = Typography

const getStatusTag = (status: KnowledgeDocumentStatus) => {
  const icon =
    status === 'pending' ? (
      <LoadingOutlined spin />
    ) : status === 'ready' ? (
      <CheckCircleOutlined />
    ) : (
      <CloseCircleOutlined />
    )

  return (
    <Tag color={KNOWLEDGE_STATUS_COLORS[status]} icon={icon} style={{ margin: 0 }}>
      {KNOWLEDGE_STATUS_LABELS[status]}
    </Tag>
  )
}

/**
 * The workspace's knowledge base (Phase 14.7 §B).
 *
 * <p><b>Everything here is a request, not a result.</b> Adding and removing a document each record a PENDING
 * AI action that a Manager must approve, so the page never says a document "was added" or "was deleted" —
 * the wording is the contract, and a success toast that overstates it would make the approval step look like
 * it had already happened.</p>
 *
 * <p><b>`enabled === false` is not an empty list.</b> It means `Jev:RagEnabled` is off on the server, and the
 * page says that instead of rendering "no documents yet" — a message the user cannot act on and that hides a
 * switched-off feature.</p>
 */
export const KnowledgeBasePage: React.FC = () => {
  const { workspaceId = '' } = useParams<{ workspaceId: string }>()
  const navigate = useNavigate()
  const { isManagerOrAdmin } = useWorkspaceRole(workspaceId)
  const { documents, enabled, loading, error, refetch, create, remove, clearError } =
    useKnowledgeDocuments(workspaceId)

  const [formOpen, setFormOpen] = useState(false)

  const canManage = isManagerOrAdmin && enabled

  const handleCreate = async (request: CreateKnowledgeDocumentRequest): Promise<boolean> => {
    const log = await create(request)

    if (!log) {
      return false
    }

    // Never "đã thêm tài liệu": the server answered with an action, and a Manager still has to approve it.
    message.success('Đã gửi yêu cầu thêm tài liệu — chờ Manager duyệt trong Lịch sử AI')
    return true
  }

  const handleDelete = async (documentId: string) => {
    const log = await remove(documentId)

    if (log) {
      message.success('Đã gửi yêu cầu xoá tài liệu — chờ Manager duyệt trong Lịch sử AI')
    }
  }

  const columns: ColumnsType<KnowledgeDocument> = [
    {
      title: 'Tài liệu',
      dataIndex: 'title',
      key: 'title',
      render: (title: string) => (
        <Text strong style={{ fontSize: 13 }}>
          {title}
        </Text>
      ),
    },
    {
      title: 'Trạng thái',
      dataIndex: 'status',
      key: 'status',
      width: 150,
      render: (status: KnowledgeDocumentStatus) => getStatusTag(status),
    },
    {
      title: 'Số đoạn',
      dataIndex: 'chunkCount',
      key: 'chunkCount',
      width: 100,
      align: 'right',
    },
    {
      title: 'Nguồn',
      dataIndex: 'sourceUrl',
      key: 'sourceUrl',
      width: 220,
      render: (sourceUrl: string | null) =>
        sourceUrl ? (
          <Typography.Link
            href={sourceUrl}
            target="_blank"
            rel="noreferrer noopener"
            style={{ fontSize: 12, wordBreak: 'break-all' }}
          >
            {sourceUrl}
          </Typography.Link>
        ) : (
          <Text type="secondary">—</Text>
        ),
    },
    {
      title: 'Ngày tạo',
      dataIndex: 'createdAt',
      key: 'createdAt',
      width: 150,
      render: (createdAt: string) => (createdAt ? dayjs(createdAt).format('DD/MM/YYYY HH:mm') : '—'),
    },
    {
      // The only place a failed ingest explains itself. For a healthy document there is nothing to say, so
      // the column stays empty rather than repeating "không có lỗi".
      title: 'Lỗi',
      dataIndex: 'error',
      key: 'error',
      width: 220,
      render: (docError: string | null, record) =>
        record.status === 'failed' && docError ? (
          <Text type="danger" style={{ fontSize: 12 }}>
            {docError}
          </Text>
        ) : (
          <Text type="secondary">—</Text>
        ),
    },
    {
      title: 'Hành động',
      key: 'actions',
      width: 110,
      align: 'center',
      render: (_, record) => (
        <Tooltip
          title={
            !enabled
              ? 'Bộ nhớ ngữ nghĩa đang tắt nên không thể xoá tài liệu'
              : !isManagerOrAdmin
                ? 'Chỉ Manager hoặc Admin mới có quyền xoá tài liệu'
                : 'Gửi yêu cầu xoá tài liệu (cần Manager duyệt)'
          }
        >
          <Popconfirm
            title="Gửi yêu cầu xoá tài liệu?"
            description="Tài liệu chỉ bị xoá sau khi Manager duyệt yêu cầu này."
            okText="Gửi yêu cầu"
            cancelText="Đóng"
            okButtonProps={{ danger: true }}
            disabled={!canManage}
            onConfirm={() => handleDelete(record.id)}
          >
            <Button
              danger
              type="text"
              size="small"
              icon={<DeleteOutlined />}
              disabled={!canManage}
              aria-label={`Xoá tài liệu ${record.title}`}
              data-testid={`knowledge-delete-${record.id}`}
            >
              Xoá
            </Button>
          </Popconfirm>
        </Tooltip>
      ),
    },
  ]

  return (
    <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
      <AppHeader workspaceId={workspaceId} />

      <div
        style={{
          backgroundColor: '#ffffff',
          borderBottom: '1px solid #e2e8f0',
          padding: '0 24px',
          minHeight: 64,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          flexWrap: 'wrap',
          gap: 12,
        }}
      >
        <Flex align="center" gap={12}>
          <Button
            type="text"
            icon={<ArrowLeftOutlined />}
            aria-label="Quay lại danh sách bảng"
            onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}
          />
          <Typography.Title level={4} style={{ margin: 0, color: '#0f172a', fontWeight: 700 }}>
            <DatabaseOutlined style={{ marginRight: 8, color: '#4f46e5' }} />
            Kho tri thức
          </Typography.Title>
          <Tag
            style={{
              margin: 0,
              borderRadius: 999,
              fontSize: 11,
              fontWeight: 600,
              color: '#7c3aed',
              backgroundColor: '#f5f3ff',
              borderColor: '#ddd6fe',
              fontFamily: "'JetBrains Mono', monospace",
            }}
          >
            pgvector 384-dim RAG
          </Tag>
        </Flex>

        <Space wrap>
          <Button icon={<ReloadOutlined />} onClick={() => void refetch()} loading={loading} style={{ borderRadius: 8 }}>
            Làm mới
          </Button>
          <Tooltip
            title={
              !enabled
                ? 'Bộ nhớ ngữ nghĩa đang tắt (Jev:RagEnabled)'
                : !isManagerOrAdmin
                  ? 'Chỉ Manager hoặc Admin mới có quyền thêm tài liệu'
                  : ''
            }
          >
            <Button
              type="primary"
              icon={<PlusOutlined />}
              disabled={!canManage}
              onClick={() => setFormOpen(true)}
              data-testid="knowledge-add-document"
              style={{
                backgroundColor: canManage ? '#4f46e5' : undefined,
                borderColor: canManage ? '#4f46e5' : undefined,
                borderRadius: 8,
                fontWeight: 600,
              }}
            >
              Thêm tài liệu
            </Button>
          </Tooltip>
        </Space>
      </div>

      <Content style={{ padding: '24px 32px', maxWidth: 1200, margin: '0 auto', width: '100%' }}>
        {!enabled && (
          <Alert
            type="warning"
            showIcon
            data-testid="knowledge-disabled-alert"
            // `title`, not the deprecated `message`.
            title="Bộ nhớ ngữ nghĩa đang tắt (Jev:RagEnabled)"
            description="Tính năng này đang bị tắt trên máy chủ, nên danh sách tài liệu không được tải và không thể thêm hay xoá tài liệu. Hãy bật cấu hình Jev:RagEnabled rồi mở lại trang."
            style={{ marginBottom: 16 }}
          />
        )}

        {error && (
          <Alert
            type="error"
            showIcon
            closable
            data-testid="knowledge-error-alert"
            title="Không thực hiện được yêu cầu"
            // The server's own message, verbatim: it names the rule that was broken.
            description={<span data-testid="knowledge-error-message">{error}</span>}
            onClose={clearError}
            style={{ marginBottom: 16 }}
          />
        )}

        {enabled && (
          <>
            <Row gutter={[12, 12]} style={{ marginBottom: 16 }}>
              <Col xs={24} sm={8}>
                <div
                  style={{
                    padding: '12px 16px',
                    borderRadius: 10,
                    backgroundColor: '#ffffff',
                    border: '1px solid #e2e8f0',
                    borderTop: '3px solid #4f46e5',
                  }}
                >
                  <Text type="secondary" style={{ fontSize: 11.5, display: 'block' }}>
                    TỔNG TÀI LIỆU NGỮ NGHĨA
                  </Text>
                  <Text strong style={{ fontSize: 18, color: '#0f172a', fontFamily: "'JetBrains Mono', monospace" }}>
                    {documents.length} tài liệu
                  </Text>
                </div>
              </Col>
              <Col xs={24} sm={8}>
                <div
                  style={{
                    padding: '12px 16px',
                    borderRadius: 10,
                    backgroundColor: '#ffffff',
                    border: '1px solid #e2e8f0',
                    borderTop: '3px solid #10b981',
                  }}
                >
                  <Text type="secondary" style={{ fontSize: 11.5, display: 'block' }}>
                    VECTOR CHUNKS ĐÃ LẬP CHỈ MỤC
                  </Text>
                  <Text strong style={{ fontSize: 18, color: '#059669', fontFamily: "'JetBrains Mono', monospace" }}>
                    {documents.reduce((sum, d) => sum + (d.chunkCount || 0), 0)} chunks
                  </Text>
                </div>
              </Col>
              <Col xs={24} sm={8}>
                <div
                  style={{
                    padding: '12px 16px',
                    borderRadius: 10,
                    backgroundColor: '#ffffff',
                    border: '1px solid #e2e8f0',
                    borderTop: '3px solid #7c3aed',
                  }}
                >
                  <Text type="secondary" style={{ fontSize: 11.5, display: 'block' }}>
                    CƠ CHẾ KIỂM SOÁT AI
                  </Text>
                  <Text strong style={{ fontSize: 13.5, color: '#6d28d9' }}>
                    Human-in-the-Loop (Manager Approval)
                  </Text>
                </div>
              </Col>
            </Row>

            <Alert
              type="info"
              showIcon
              title="Mọi thay đổi tài liệu đều cần Manager duyệt"
              description="Yêu cầu thêm hoặc xoá tài liệu được ghi thành một đề xuất chờ duyệt trong Lịch sử AI; bộ nhớ chỉ thay đổi sau khi Manager phê duyệt."
              style={{ marginBottom: 16 }}
            />
          </>
        )}

        <Card style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}>
          <Table<KnowledgeDocument>
            dataSource={documents}
            columns={columns}
            rowKey="id"
            loading={loading}
            pagination={false}
            scroll={{ x: 'max-content' }}
            // With the feature off — or with the list never loaded — the table's own "no data" text would claim
            // the workspace has no documents. That is only knowable when the request actually succeeded, so the
            // empty state is shown on exactly that condition and the alerts above explain the other two.
            locale={{
              emptyText:
                enabled && !error ? (
                  <Empty
                    image={Empty.PRESENTED_IMAGE_SIMPLE}
                    description="Chưa có tài liệu nào trong bộ nhớ ngữ nghĩa."
                  />
                ) : null,
            }}
          />
        </Card>
      </Content>

      <KnowledgeDocumentForm
        open={formOpen}
        onCancel={() => setFormOpen(false)}
        onSubmit={handleCreate}
      />
    </Layout>
  )
}
