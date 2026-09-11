import React, { useCallback, useEffect, useState } from 'react'
import {
  DownloadOutlined,
  FileOutlined,
  FileTextOutlined,
  PaperClipOutlined,
  SyncOutlined,
} from '@ant-design/icons'
import { Button, Empty, Flex, List, Spin, Tag, Typography, message } from 'antd'
import { agentApi } from '../services/agentApi'
import type { AttachmentResponse } from '../types/agentRun.types'
import { formatBytes } from '../utils/agentRunHelpers'

interface AttachmentListProps {
  taskId: string
  attachments?: AttachmentResponse[]
  onRefresh?: () => void
}

export const AttachmentList: React.FC<AttachmentListProps> = ({
  taskId,
  attachments: externalAttachments,
  onRefresh,
}) => {
  const [internalAttachments, setInternalAttachments] = useState<AttachmentResponse[]>([])
  const [loading, setLoading] = useState<boolean>(false)
  const [downloadingId, setDownloadingId] = useState<string | null>(null)

  const fetchAttachments = useCallback(async () => {
    if (!taskId) return
    setLoading(true)
    try {
      const data = await agentApi.listAttachments(taskId)
      setInternalAttachments(data)
    } catch {
      message.error('Không thể tải danh sách tệp đính kèm')
    } finally {
      setLoading(false)
    }
  }, [taskId])

  useEffect(() => {
    if (externalAttachments === undefined) {
      Promise.resolve().then(() => {
        fetchAttachments()
      })
    }
  }, [externalAttachments, fetchAttachments])

  const items = externalAttachments !== undefined ? externalAttachments : internalAttachments

  const handleDownload = async (item: AttachmentResponse) => {
    setDownloadingId(item.id)
    try {
      await agentApi.downloadAttachment(taskId, item.id, item.fileName)
    } catch {
      message.error('Tải tệp thất bại')
    } finally {
      setDownloadingId(null)
    }
  }

  return (
    <div data-testid="attachment-list">
      <Flex justify="space-between" align="center" style={{ marginBottom: 12 }}>
        <Flex align="center" gap={6}>
          <PaperClipOutlined style={{ color: '#6366f1' }} />
          <Typography.Text strong style={{ fontSize: 13 }}>
            Tệp đính kèm ({items.length})
          </Typography.Text>
        </Flex>
        <Button
          size="small"
          type="text"
          icon={<SyncOutlined spin={loading} />}
          onClick={() => {
            fetchAttachments()
            onRefresh?.()
          }}
        >
          Làm mới
        </Button>
      </Flex>

      {loading && items.length === 0 ? (
        <Flex justify="center" style={{ padding: '24px 0' }}>
          <Spin size="small" />
        </Flex>
      ) : items.length === 0 ? (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description="Chưa có tệp đính kèm nào"
          style={{ margin: '16px 0' }}
        />
      ) : (
        <List
          size="small"
          dataSource={items}
          renderItem={(item) => (
            <List.Item
              key={item.id}
              data-testid={`attachment-item-${item.id}`}
              style={{
                padding: '8px 12px',
                marginBottom: 6,
                background: '#f8fafc',
                border: '1px solid #e2e8f0',
                borderRadius: 6,
              }}
              actions={[
                <Button
                  key="download"
                  type="primary"
                  ghost
                  size="small"
                  icon={<DownloadOutlined />}
                  loading={downloadingId === item.id}
                  onClick={() => handleDownload(item)}
                  data-testid={`download-btn-${item.id}`}
                >
                  Tải xuống
                </Button>,
              ]}
            >
              <List.Item.Meta
                avatar={
                  item.contentType.includes('text') || item.contentType.includes('markdown') ? (
                    <FileTextOutlined style={{ fontSize: 20, color: '#6366f1', marginTop: 4 }} />
                  ) : (
                    <FileOutlined style={{ fontSize: 20, color: '#64748b', marginTop: 4 }} />
                  )
                }
                title={
                  <Flex align="center" gap={8} wrap="wrap">
                    <Typography.Text strong style={{ fontSize: 13 }}>
                      {item.fileName}
                    </Typography.Text>
                    {item.sourceRunId && (
                      <Tag color="purple" style={{ margin: 0, fontSize: 11 }}>
                        do AI Agent tạo
                      </Tag>
                    )}
                  </Flex>
                }
                description={
                  <Flex gap={12} style={{ fontSize: 11, color: '#64748b' }}>
                    <span>{formatBytes(item.sizeBytes)}</span>
                    <span>Người tạo: {item.createdByName}</span>
                  </Flex>
                }
              />
            </List.Item>
          )}
        />
      )}
    </div>
  )
}
