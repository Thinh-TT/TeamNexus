import React, { useEffect, useMemo, useState } from 'react'
import {
  ArrowLeftOutlined,
  FilterOutlined,
  HistoryOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import {
  Button,
  Card,
  Empty,
  Flex,
  Layout,
  Result,
  Segmented,
  Select,
  Space,
  Spin,
  Typography,
} from 'antd'
import { useNavigate, useParams } from 'react-router-dom'
import { useWorkspaceRole } from '../../../shared/hooks/useWorkspaceRole'
import { boardApi } from '../../board/services/boardApi'
import type { BoardResponse } from '../../board/types/board.types'
import { ActivityFeedItem } from '../components/ActivityFeedItem'
import { useWorkspaceActivity } from '../hooks/useWorkspaceActivity'
import { AppHeader } from '../../../shared/components/AppHeader'

const { Content } = Layout

export const WorkspaceActivityPage: React.FC = () => {
  const { workspaceId = '' } = useParams<{ workspaceId: string }>()
  const navigate = useNavigate()

  const { isManagerOrAdmin, loading: roleLoading } = useWorkspaceRole(workspaceId)

  // Filter state
  const [entityFilter, setEntityFilter] = useState<string>('ALL')
  const [selectedBoardId, setSelectedBoardId] = useState<string | undefined>(undefined)

  // Boards list for filter dropdown
  const [boards, setBoards] = useState<BoardResponse[]>([])

  useEffect(() => {
    if (!workspaceId) return
    boardApi
      .getBoards(workspaceId)
      .then(setBoards)
      .catch(() => {})
  }, [workspaceId])

  const filterParams = useMemo(
    () => ({
      entityType: entityFilter === 'ALL' ? undefined : entityFilter,
      boardId: selectedBoardId,
    }),
    [entityFilter, selectedBoardId]
  )

  const {
    items,
    hasMore,
    loading: activityLoading,
    loadingMore,
    loadMore,
    reload,
  } = useWorkspaceActivity(workspaceId, filterParams)

  if (roleLoading) {
    return (
      <Flex align="center" justify="center" style={{ minHeight: '60vh' }}>
        <Spin size="large" />
      </Flex>
    )
  }

  if (!isManagerOrAdmin) {
    return (
      <Result
        status="403"
        title="403"
        subTitle="Bạn không có quyền xem nhật ký hoạt động. Chỉ Manager hoặc Admin mới có quyền truy cập."
        extra={
          <Button type="primary" onClick={() => navigate(`/workspaces/${workspaceId}/boards`)}>
            Quay lại bảng làm việc
          </Button>
        }
      />
    )
  }

  const boardNameMap = new Map(boards.map((b) => [b.id, b.name]))

  return (
    <Layout style={{ minHeight: '100vh', backgroundColor: '#f8fafc' }}>
      <AppHeader workspaceId={workspaceId} />

      <div
        style={{
          backgroundColor: '#ffffff',
          borderBottom: '1px solid #e2e8f0',
          padding: '0 24px',
          height: 64,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
        }}
      >
        <Flex align="center" gap={12}>
          <Button
            type="text"
            icon={<ArrowLeftOutlined />}
            aria-label="Quay lại"
            data-testid="back-to-boards-btn"
            onClick={() => {
              if (workspaceId) {
                navigate(`/workspaces/${workspaceId}/boards`)
              } else {
                navigate(-1)
              }
            }}
          />
          <Typography.Title level={4} style={{ margin: 0, color: '#0f172a' }}>
            <HistoryOutlined style={{ marginRight: 8, color: '#6366f1' }} />
            Lịch sử hoạt động workspace
          </Typography.Title>
        </Flex>

        <Space>
          <Button
            icon={<ReloadOutlined />}
            onClick={reload}
            loading={activityLoading && items.length === 0}
          >
            Làm mới
          </Button>
        </Space>
      </div>

      <Content style={{ padding: '24px 32px', maxWidth: 900, margin: '0 auto', width: '100%' }}>
        {/* Filters Card */}
        <Card
          size="small"
          style={{
            borderRadius: 12,
            border: '1px solid #e2e8f0',
            marginBottom: 20,
            backgroundColor: '#ffffff',
          }}
        >
          <Flex justify="space-between" align="center" wrap="wrap" gap={12}>
            <Space size={12} wrap>
              <Typography.Text type="secondary">
                <FilterOutlined style={{ marginRight: 4 }} />
                Phân loại:
              </Typography.Text>
              <Segmented
                value={entityFilter}
                onChange={(val) => setEntityFilter(val as string)}
                data-testid="activity-entity-filter"
                options={[
                  { label: 'Tất cả', value: 'ALL' },
                  { label: 'Thẻ', value: 'Task' },
                  { label: 'Bình luận', value: 'Comment' },
                  { label: 'Workspace', value: 'Workspace' },
                ]}
              />
            </Space>

            <Space size={12}>
              <Typography.Text type="secondary">Bảng:</Typography.Text>
              <Select
                style={{ width: 200 }}
                placeholder="Tất cả các bảng"
                allowClear
                value={selectedBoardId}
                onChange={setSelectedBoardId}
                data-testid="activity-board-filter"
                options={boards.map((b) => ({
                  value: b.id,
                  label: b.name,
                }))}
              />
            </Space>
          </Flex>
        </Card>

        {/* Activity List */}
        {activityLoading ? (
          <Flex align="center" justify="center" style={{ padding: '64px 0' }}>
            <Spin size="large" tip="Đang tải lịch sử hoạt động..." />
          </Flex>
        ) : items.length === 0 ? (
          <Card style={{ borderRadius: 12, padding: 48, textAlign: 'center' }}>
            <Empty description="Chưa có hoạt động nào được ghi nhận" />
          </Card>
        ) : (
          <div>
            {items.map((item) => (
              <ActivityFeedItem
                key={item.id}
                item={item}
                boardName={item.boardId ? boardNameMap.get(item.boardId) : undefined}
              />
            ))}

            {hasMore && (
              <Flex justify="center" style={{ marginTop: 20 }}>
                <Button
                  onClick={loadMore}
                  loading={loadingMore}
                  style={{ borderRadius: 8 }}
                  data-testid="load-more-activity-btn"
                >
                  Tải thêm hoạt động
                </Button>
              </Flex>
            )}
          </div>
        )}
      </Content>
    </Layout>
  )
}
