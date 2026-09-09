import React from 'react'
import { useParams } from 'react-router-dom'
import { Result, Button } from 'antd'
import { useNavigate } from 'react-router-dom'
import { BoardView } from '../components/BoardView'

export const BoardPage: React.FC = () => {
  const { workspaceId, boardId } = useParams<{ workspaceId: string; boardId: string }>()
  const navigate = useNavigate()

  if (!workspaceId || !boardId) {
    return (
      <Result
        status="404"
        title="Không tìm thấy bảng"
        subTitle="Đường dẫn không hợp lệ hoặc thiếu thông tin workspace/board."
        extra={
          <Button type="primary" onClick={() => navigate('/')}>
            Quay lại trang chủ
          </Button>
        }
      />
    )
  }

  return <BoardView workspaceId={workspaceId} boardId={boardId} />
}
