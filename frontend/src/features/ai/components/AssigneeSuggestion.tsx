import React, { useState } from 'react'
import {
  ThunderboltOutlined,
  UserOutlined,
  WarningOutlined,
  LoadingOutlined,
  DownOutlined,
  UpOutlined,
  ClockCircleOutlined,
  InfoCircleOutlined,
} from '@ant-design/icons'
import { jevApi } from '../services/jevApi'
import type { AssigneeSuggestionResponse } from '../types/jev.types'
import {
  formatConfidencePercent,
  getAdviceBadgeClass,
  isDirectAssign,
  isManualRequired,
  isOfferChoices,
} from '../utils/confidenceHelpers'

interface AssigneeSuggestionProps {
  boardId: string
  taskId: string
  currentAssigneeId?: string | null
  onSelectAssignee: (userId: string) => void
}

export const AssigneeSuggestion: React.FC<AssigneeSuggestionProps> = ({
  boardId,
  taskId,
  currentAssigneeId,
  onSelectAssignee,
}) => {
  const [isOpen, setIsOpen] = useState(false)
  const [isLoading, setIsLoading] = useState(false)
  const [data, setData] = useState<AssigneeSuggestionResponse | null>(null)
  const [error, setError] = useState<string | null>(null)

  const handleFetchSuggestions = async () => {
    if (isOpen && data) {
      setIsOpen(false)
      return
    }

    setIsOpen(true)
    setIsLoading(true)
    setError(null)

    try {
      const res = await jevApi.suggestAssignees(boardId, taskId)
      setData(res)
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } }
      setError(axiosErr.response?.data?.message || 'Không thể lấy gợi ý người làm.')
    } finally {
      setIsLoading(false)
    }
  }

  return (
    <div className="space-y-2" data-testid="assignee-suggestion">
      <button
        type="button"
        onClick={handleFetchSuggestions}
        className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-medium rounded-lg bg-indigo-500/10 hover:bg-indigo-500/20 text-indigo-400 border border-indigo-500/20 transition-all duration-150"
      >
        <ThunderboltOutlined style={{ fontSize: 14 }} className="text-indigo-400" />
        <span>Gợi ý người làm (Jev)</span>
        {isOpen ? (
          <UpOutlined style={{ fontSize: 10 }} className="ml-0.5" />
        ) : (
          <DownOutlined style={{ fontSize: 10 }} className="ml-0.5" />
        )}
      </button>

      {isOpen && (
        <div className="p-3 rounded-xl bg-slate-900/80 border border-slate-800 shadow-lg backdrop-blur-md space-y-3 animate-in fade-in slide-in-from-top-1 duration-200">
          {isLoading && (
            <div className="flex items-center gap-2 py-3 justify-center text-xs text-indigo-300">
              <LoadingOutlined style={{ fontSize: 16 }} className="text-indigo-400" />
              <span>Jev đang đối soát hồ sơ kỹ năng và tải việc...</span>
            </div>
          )}

          {error && !isLoading && (
            <div className="flex items-center gap-2 p-2 rounded-lg bg-rose-500/10 border border-rose-500/20 text-rose-300 text-xs">
              <WarningOutlined style={{ fontSize: 16 }} className="text-rose-400 shrink-0" />
              <span>{error}</span>
            </div>
          )}

          {data && !isLoading && (
            <>
              <div className="flex items-center justify-between text-xs pb-2 border-b border-slate-800">
                <div className="flex items-center gap-2">
                  <span className="font-medium text-slate-300">Đánh giá phân công</span>
                  <span
                    className={`text-[10px] font-medium px-1.5 py-0.5 rounded border ${getAdviceBadgeClass(
                      data.routingAdvice
                    )}`}
                  >
                    {isDirectAssign(data.routingAdvice)
                      ? `Tự tin ${formatConfidencePercent(data.confidenceScore)}`
                      : isOfferChoices(data.routingAdvice)
                      ? `Lựa chọn (${formatConfidencePercent(data.confidenceScore)})`
                      : 'Cần phân công thủ công'}
                  </span>
                </div>

                {data.latencyMs > 0 && (
                  <div className="flex items-center gap-1 text-[11px] text-slate-400 font-mono">
                    <ClockCircleOutlined style={{ fontSize: 12 }} className="text-slate-400" />
                    <span>{data.latencyMs}ms</span>
                  </div>
                )}
              </div>

              {data.degraded && (
                <div className="flex items-center gap-2 p-2 rounded-lg bg-amber-500/10 border border-amber-500/20 text-amber-300 text-xs">
                  <WarningOutlined style={{ fontSize: 14 }} className="text-amber-400 shrink-0" />
                  <span>Dịch vụ AI tạm không khả dụng. Bạn hãy phân công thủ công.</span>
                </div>
              )}

              {/* Nhánh 1: DirectAssign */}
              {isDirectAssign(data.routingAdvice) && data.recommendedUserId && (
                <div className="space-y-2">
                  {data.candidates
                    .filter((c) => c.userId === data.recommendedUserId)
                    .map((candidate) => (
                      <div
                        key={candidate.userId}
                        className="p-2.5 rounded-lg bg-emerald-950/20 border border-emerald-500/30 flex items-center justify-between gap-3"
                      >
                        <div className="space-y-1 min-w-0">
                          <div className="flex items-center gap-2">
                            <span className="font-semibold text-xs text-emerald-300 truncate">
                              {candidate.displayName}
                            </span>
                            <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-emerald-500/20 text-emerald-300 font-medium">
                              {Math.round(candidate.probability * 100)}% phù hợp
                            </span>
                          </div>
                          {candidate.reason && (
                            <p className="text-[11px] text-slate-400 line-clamp-2">
                              {candidate.reason}
                            </p>
                          )}
                        </div>

                        <button
                          type="button"
                          onClick={() => {
                            onSelectAssignee(candidate.userId)
                            setIsOpen(false)
                          }}
                          disabled={candidate.userId === currentAssigneeId}
                          className="shrink-0 flex items-center gap-1 px-3 py-1.5 rounded-lg bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white text-xs font-medium transition-colors"
                        >
                          <UserOutlined style={{ fontSize: 14 }} />
                          <span>{candidate.userId === currentAssigneeId ? 'Đang gán' : 'Gán ngay'}</span>
                        </button>
                      </div>
                    ))}
                </div>
              )}

              {/* Nhánh 2: OfferChoices */}
              {isOfferChoices(data.routingAdvice) && data.candidates.length > 0 && (
                <div className="space-y-2">
                  <p className="text-[11px] text-slate-400">
                    Jev đề xuất danh sách ứng viên phù hợp kỹ năng và khối lượng việc:
                  </p>
                  <div className="space-y-1.5">
                    {data.candidates.map((cand) => (
                      <div
                        key={cand.userId}
                        className="p-2 rounded-lg bg-slate-800/40 hover:bg-slate-800/70 border border-slate-700/50 flex items-center justify-between gap-2 transition-colors"
                      >
                        <div className="min-w-0 space-y-0.5">
                          <div className="flex items-center gap-1.5">
                            <span className="font-medium text-xs text-slate-200 truncate">
                              {cand.displayName}
                            </span>
                            <span className="text-[10px] font-mono text-indigo-400 bg-indigo-950/40 px-1 rounded">
                              {Math.round(cand.probability * 100)}%
                            </span>
                          </div>
                          {cand.reason && (
                            <div className="flex items-center gap-1 text-[11px] text-slate-400 truncate">
                              <InfoCircleOutlined style={{ fontSize: 12 }} className="text-slate-400 shrink-0" />
                              <span className="truncate">{cand.reason}</span>
                            </div>
                          )}
                        </div>

                        <button
                          type="button"
                          onClick={() => {
                            onSelectAssignee(cand.userId)
                            setIsOpen(false)
                          }}
                          disabled={cand.userId === currentAssigneeId}
                          className="shrink-0 px-2.5 py-1 text-xs rounded-md bg-slate-700 hover:bg-indigo-600 text-slate-200 hover:text-white disabled:opacity-40 transition-colors"
                        >
                          {cand.userId === currentAssigneeId ? 'Đang gán' : 'Chọn'}
                        </button>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Nhánh 3: ManualRequired */}
              {isManualRequired(data.routingAdvice) && (
                <div className="p-2.5 rounded-lg bg-slate-800/30 border border-slate-700/40 text-xs text-slate-400">
                  <span>
                    Chưa có ứng viên đạt độ tương thích cao. Vui lòng tự chọn thành viên từ danh sách.
                  </span>
                </div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  )
}
