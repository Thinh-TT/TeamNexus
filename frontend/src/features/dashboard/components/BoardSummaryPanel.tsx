import React from 'react'
import { Alert, Card, Empty, Flex, List, Progress, Space, Tag, Typography } from 'antd'
import { useNavigate } from 'react-router-dom'
import type { DashboardBoardSummary } from '../types/dashboard.types'

interface BoardSummaryPanelProps {
  workspaceId: string
  boards: DashboardBoardSummary[]
  boardsTruncated: boolean
}

export const BoardSummaryPanel: React.FC<BoardSummaryPanelProps> = ({
  workspaceId,
  boards,
  boardsTruncated,
}) => {
  const navigate = useNavigate()

  return (
    <Card
      title="Tóm tắt bảng Kanban"
      style={{ borderRadius: 12, border: '1px solid #e2e8f0' }}
      styles={{ body: { padding: '16px 20px' } }}
    >
      {boardsTruncated && (
        <Alert
          message="Workspace có nhiều bảng: chỉ hiển thị 20 bảng đầu tiên."
          type="info"
          showIcon
          style={{ marginBottom: 16, borderRadius: 8 }}
        />
      )}

      {boards.length === 0 ? (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description="Chưa có bảng nào trong workspace"
          style={{ margin: '24px 0' }}
        />
      ) : (
        <List
          itemLayout="vertical"
          dataSource={boards}
          renderItem={(board) => {
            const denominator = board.done + board.open
            const percent =
              denominator > 0 ? Math.round((board.done / denominator) * 100) : 0

            return (
              <List.Item
                key={board.boardId}
                style={{
                  padding: '16px 0',
                  borderBottom: '1px solid #f1f5f9',
                }}
              >
                <Flex vertical gap={10}>
                  <Flex justify="space-between" align="center" wrap="wrap" gap={8}>
                    <Typography.Link
                      strong
                      style={{ fontSize: 15, color: '#0f172a' }}
                      onClick={() =>
                        navigate(`/workspaces/${workspaceId}/boards/${board.boardId}`)
                      }
                    >
                      {board.name}
                    </Typography.Link>

                    <Space size={12} wrap>
                      <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                        Tổng: <strong>{board.total}</strong>
                      </Typography.Text>
                      <Typography.Text type="success" style={{ fontSize: 13 }}>
                        Đã xong: <strong>{board.done}</strong>
                      </Typography.Text>
                      <Typography.Text type="secondary" style={{ fontSize: 13 }}>
                        Đang mở: <strong>{board.open}</strong>
                      </Typography.Text>
                      {board.overdue > 0 && (
                        <Typography.Text type="danger" style={{ fontSize: 13 }}>
                          Quá hạn: <strong>{board.overdue}</strong>
                        </Typography.Text>
                      )}
                    </Space>
                  </Flex>

                  <Progress
                    percent={percent}
                    size="small"
                    strokeColor="#6366f1"
                    status={percent === 100 ? 'success' : 'normal'}
                  />

                  {board.columns && board.columns.length > 0 && (
                    <Flex wrap="wrap" gap={6}>
                      {board.columns.map((col) => (
                        <Tag
                          key={col.columnId}
                          color={col.isDone ? 'green' : 'default'}
                          style={{ borderRadius: 4, margin: 0, fontSize: 12 }}
                        >
                          {col.name}: {col.count}
                        </Tag>
                      ))}
                    </Flex>
                  )}
                </Flex>
              </List.Item>
            )
          }}
        />
      )}
    </Card>
  )
}
