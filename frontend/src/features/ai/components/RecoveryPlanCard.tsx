import React, { useState } from 'react'
import {
  ToolOutlined,
  WarningOutlined,
  UserOutlined,
  CalendarOutlined,
  BranchesOutlined,
  ExclamationCircleOutlined,
  CheckOutlined,
  CloseOutlined,
  UndoOutlined,
  LoadingOutlined,
} from '@ant-design/icons'
import { aiActionApi } from '../services/aiActionApi'
import type { AiActionLog, AiActionLogDetail } from '../types/aiAction.types'
import type { RecoveryPlanSnapshot } from '../types/jev.types'

interface RecoveryPlanCardProps {
  actionLog: AiActionLog | AiActionLogDetail
  detail?: AiActionLogDetail | null
  onActionSuccess?: (updated: AiActionLogDetail) => void
}

export const RecoveryPlanCard: React.FC<RecoveryPlanCardProps> = ({
  actionLog,
  detail,
  onActionSuccess,
}) => {
  const [isActing, setIsActing] = useState(false)
  const [error, setError] = useState<string | null>(null)

  let snapshot: RecoveryPlanSnapshot | null = null
  try {
    const raw =
      (detail as unknown as Record<string, unknown>)?.afterSnapshotJson ||
      (actionLog as unknown as Record<string, unknown>)?.basisJson ||
      detail?.afterSnapshot ||
      detail?.basis
    if (typeof raw === 'string') {
      snapshot = JSON.parse(raw) as RecoveryPlanSnapshot
    } else if (raw && typeof raw === 'object') {
      snapshot = raw as unknown as RecoveryPlanSnapshot
    }
  } catch {
    snapshot = null
  }

  const kind = snapshot?.kind || 'ReassignTask'
  const isEscalate = kind === 'EscalateManager'

  const handleApprove = async () => {
    setIsActing(true)
    setError(null)
    try {
      const updated = await aiActionApi.approveAiAction(actionLog.id)
      onActionSuccess?.(updated)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } }
      setError(axiosErr.response?.data?.message || 'Không thể phê duyệt kế hoạch phục hồi.')
    } finally {
      setIsActing(false)
    }
  }

  const handleReject = async () => {
    setIsActing(true)
    setError(null)
    try {
      const updated = await aiActionApi.rejectAiAction(actionLog.id)
      onActionSuccess?.(updated)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } }
      setError(axiosErr.response?.data?.message || 'Không thể từ chối kế hoạch phục hồi.')
    } finally {
      setIsActing(false)
    }
  }

  const handleUndo = async () => {
    setIsActing(true)
    setError(null)
    try {
      const updated = await aiActionApi.undoAiAction(actionLog.id)
      onActionSuccess?.(updated)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } }
      setError(axiosErr.response?.data?.message || 'Không thể hoàn tác hành động.')
    } finally {
      setIsActing(false)
    }
  }

  const renderKindIcon = () => {
    switch (kind) {
      case 'ReassignTask':
        return <UserOutlined style={{ fontSize: 16 }} className="text-sky-400" />
      case 'ExtendDeadline':
        return <CalendarOutlined style={{ fontSize: 16 }} className="text-emerald-400" />
      case 'SplitSubtasks':
        return <BranchesOutlined style={{ fontSize: 16 }} className="text-purple-400" />
      case 'EscalateManager':
      default:
        return <ExclamationCircleOutlined style={{ fontSize: 16 }} className="text-amber-400" />
    }
  }

  const getKindTitle = () => {
    switch (kind) {
      case 'ReassignTask':
        return 'Tái phân công người phụ trách thẻ'
      case 'ExtendDeadline':
        return 'Gia hạn thời hạn hoàn thành thẻ'
      case 'SplitSubtasks':
        return 'Tách nhỏ công việc thành các subtasks'
      case 'EscalateManager':
        return 'Báo cáo khẩn cấp cấp quản lý'
      default:
        return 'Kế hoạch phục hồi'
    }
  }

  return (
    <div
      data-testid="recovery-plan-card"
      className="p-3.5 rounded-xl bg-slate-900/70 border border-slate-800 space-y-3"
    >
      <div className="flex items-center justify-between gap-2 border-b border-slate-800 pb-2">
        <div className="flex items-center gap-2">
          <div className="p-1.5 rounded-lg bg-indigo-500/10 text-indigo-400 border border-indigo-500/20">
            <ToolOutlined style={{ fontSize: 16 }} />
          </div>
          <div>
            <div className="flex items-center gap-2">
              <span className="text-xs font-semibold text-slate-200">
                AI Observer: {getKindTitle()}
              </span>
              <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-slate-800 text-slate-300 border border-slate-700">
                {kind}
              </span>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-1.5">
          {renderKindIcon()}
        </div>
      </div>

      {isEscalate && (
        <div
          data-testid="escalate-manager-warning"
          className="flex items-start gap-2.5 p-2.5 rounded-lg bg-amber-500/10 border border-amber-500/30 text-amber-300 text-xs"
        >
          <WarningOutlined style={{ fontSize: 16 }} className="text-amber-400 shrink-0 mt-0.5" />
          <div className="space-y-0.5">
            <p className="font-semibold text-amber-200">Cần họp khẩn cấp</p>
            <p className="text-[11px] text-amber-300/90">
              Cần họp khẩn — không có thay đổi dữ liệu nào được áp dụng.
            </p>
          </div>
        </div>
      )}

      {snapshot?.subtasks && snapshot.subtasks.length > 0 && (
        <div className="space-y-1.5 text-xs">
          <span className="text-[11px] text-slate-400 uppercase tracking-wider">
            Các subtasks đề xuất:
          </span>
          <ul className="list-disc list-inside space-y-0.5 text-slate-300">
            {snapshot.subtasks.map((st, idx) => (
              <li key={idx} className="truncate">
                {st}
              </li>
            ))}
          </ul>
        </div>
      )}

      {snapshot?.newDueDate && (
        <div className="text-xs text-slate-300 flex items-center gap-1.5">
          <CalendarOutlined style={{ fontSize: 14 }} className="text-emerald-400" />
          <span>Hạn mới đề xuất: {new Date(snapshot.newDueDate).toLocaleDateString()}</span>
        </div>
      )}

      {error && (
        <div className="p-2 rounded-lg bg-rose-500/10 border border-rose-500/20 text-rose-300 text-xs">
          {error}
        </div>
      )}

      <div className="flex items-center justify-between gap-2 pt-1 border-t border-slate-800/80">
        <span className="text-[11px] text-slate-400">
          Trạng thái:{' '}
          <strong className="text-slate-200">{actionLog.status}</strong>
        </span>

        <div className="flex items-center gap-1.5">
          {actionLog.status === 'Pending' && (
            <>
              <button
                type="button"
                onClick={handleReject}
                disabled={isActing}
                className="inline-flex items-center gap-1 px-2.5 py-1 text-xs rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 border border-slate-700 disabled:opacity-50 transition-colors"
              >
                {isActing ? (
                  <LoadingOutlined style={{ fontSize: 12 }} />
                ) : (
                  <CloseOutlined style={{ fontSize: 12 }} className="text-rose-400" />
                )}
                <span>Từ chối</span>
              </button>

              <button
                type="button"
                onClick={handleApprove}
                disabled={isActing}
                className="inline-flex items-center gap-1 px-3 py-1 text-xs font-medium rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white disabled:opacity-50 transition-colors"
              >
                {isActing ? (
                  <LoadingOutlined style={{ fontSize: 12 }} />
                ) : (
                  <CheckOutlined style={{ fontSize: 12 }} />
                )}
                <span>Phê duyệt giải pháp</span>
              </button>
            </>
          )}

          {actionLog.status === 'Approved' && (
            <button
              type="button"
              onClick={handleUndo}
              disabled={isActing}
              className="inline-flex items-center gap-1 px-2.5 py-1 text-xs rounded-lg bg-slate-800 hover:bg-slate-700 text-slate-300 border border-slate-700 disabled:opacity-50 transition-colors"
            >
              {isActing ? (
                <LoadingOutlined style={{ fontSize: 12 }} />
              ) : (
                <UndoOutlined style={{ fontSize: 12 }} className="text-indigo-400" />
              )}
              <span>Hoàn tác (Undo)</span>
            </button>
          )}
        </div>
      </div>
    </div>
  )
}
