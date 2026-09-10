import React, { useEffect, useState } from 'react'
import {
  HistoryOutlined,
  ReloadOutlined,
} from '@ant-design/icons'
import {
  Alert,
  Badge,
  Button,
  Drawer,
  Empty,
  Flex,
  Segmented,
  Space,
  Spin,
  Typography,
} from 'antd'
import type { AiActionStatus } from '../types/aiAction.types'
import { useAiActions } from '../hooks/useAiActions'
import { AiActionLogItem } from './AiActionLogItem'

const { Text } = Typography

type FilterType = 'ALL' | AiActionStatus

interface AiActionHistoryDrawerProps {
  open: boolean
  onClose: () => void
  boardId: string
  onBoardChanged?: () => void
}

export const AiActionHistoryDrawer: React.FC<AiActionHistoryDrawerProps> = ({
  open,
  onClose,
  boardId,
  onBoardChanged,
}) => {
  const [filter, setFilter] = useState<FilterType>('ALL')

  const {
    logs,
    pendingCount,
    status,
    error,
    reload,
    fetchDetail,
    approve,
    reject,
    undo,
  } = useAiActions({ boardId, onBoardChanged })

  useEffect(() => {
    if (open && boardId) {
      const statusParam = filter === 'ALL' ? undefined : filter
      reload(statusParam)
    }
  }, [open, boardId, filter, reload])

  const handleFilterChange = (value: FilterType) => {
    setFilter(value)
  }

  const handleRefresh = () => {
    const statusParam = filter === 'ALL' ? undefined : filter
    reload(statusParam)
  }

  const isLoading = status === 'loading'
  const isProcessing =
    status === 'approving' || status === 'rejecting' || status === 'undoing'

  return (
    <Drawer
      title={
        <Flex justify="space-between" align="center">
          <Space size={8}>
            <HistoryOutlined style={{ color: '#6366f1', fontSize: 18 }} />
            <span>Lịch sử Hành động AI</span>
            {pendingCount > 0 && (
              <Badge count={pendingCount} style={{ backgroundColor: '#f59e0b' }} />
            )}
          </Space>
          <Button
            type="text"
            icon={<ReloadOutlined spin={isLoading} />}
            onClick={handleRefresh}
            title="Tải lại danh sách"
          />
        </Flex>
      }
      placement="right"
      width={520}
      open={open}
      onClose={onClose}
      destroyOnClose={false}
      styles={{
        body: { padding: '16px', backgroundColor: '#f8fafc' },
      }}
    >
      <Flex vertical gap={12}>
        {/* Filter Bar */}
        <Segmented<FilterType>
          block
          value={filter}
          onChange={handleFilterChange}
          options={[
            { label: 'Tất cả', value: 'ALL' },
            {
              label: (
                <span>
                  Chờ duyệt{' '}
                  {pendingCount > 0 && (
                    <Badge
                      count={pendingCount}
                      size="small"
                      style={{ backgroundColor: '#f59e0b', marginLeft: 4 }}
                    />
                  )}
                </span>
              ),
              value: 'Pending',
            },
            { label: 'Đã duyệt', value: 'Approved' },
            { label: 'Từ chối', value: 'Rejected' },
            { label: 'Đã hoàn tác', value: 'Undone' },
          ]}
          style={{ marginBottom: 4 }}
        />

        {/* Error Alert if any */}
        {error && (
          <Alert
            type="error"
            message={error}
            showIcon
            closable
            style={{ borderRadius: 8 }}
          />
        )}

        {/* List Content */}
        {isLoading && logs.length === 0 ? (
          <Flex align="center" justify="center" style={{ padding: '60px 0' }}>
            <Spin tip="Đang tải lịch sử hành động AI..." />
          </Flex>
        ) : logs.length === 0 ? (
          <Empty
            description={
              filter === 'ALL'
                ? 'Chưa có hành động AI nào được ghi nhận cho bảng này.'
                : `Không có hành động nào ở trạng thái '${filter}'.`
            }
            style={{ padding: '40px 0' }}
          />
        ) : (
          <div style={{ display: 'flex', flexDirection: 'column' }}>
            <Flex justify="space-between" align="center" style={{ marginBottom: 8, padding: '0 4px' }}>
              <Text type="secondary" style={{ fontSize: 12 }}>
                Hiển thị {logs.length} hành động gần nhất
              </Text>
            </Flex>

            {logs.map((log) => (
              <AiActionLogItem
                key={log.id}
                log={log}
                onApprove={approve}
                onReject={reject}
                onUndo={undo}
                onFetchDetail={fetchDetail}
                isProcessing={isProcessing}
              />
            ))}
          </div>
        )}
      </Flex>
    </Drawer>
  )
}
