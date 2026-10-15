import React from 'react'
import {
  ThunderboltOutlined,
  CheckCircleOutlined,
  WarningOutlined,
  QuestionCircleOutlined,
  SafetyCertificateOutlined,
  LoadingOutlined,
  ClockCircleOutlined,
} from '@ant-design/icons'
import type { TaskTriageResponse, TriageOption } from '../types/jev.types'
import {
  formatConfidencePercent,
  getAdviceBadgeClass,
  isDirectAssign,
  isManualRequired,
  isOfferChoices,
} from '../utils/confidenceHelpers'

interface JevTriagePanelProps {
  triageData: TaskTriageResponse | null
  isLoading: boolean
  isDegraded: boolean
  onApplyDirect?: (suggestions: {
    domain?: string | null
    workType?: string | null
    priority?: string | null
  }) => void
  onSelectOption?: (option: TriageOption) => void
}

export const JevTriagePanel: React.FC<JevTriagePanelProps> = ({
  triageData,
  isLoading,
  isDegraded,
  onApplyDirect,
  onSelectOption,
}) => {
  if (isLoading) {
    return (
      <div className="flex items-center gap-2 px-3 py-2 text-xs rounded-lg bg-indigo-950/20 border border-indigo-500/20 text-indigo-300 animate-pulse">
        <LoadingOutlined style={{ fontSize: 14 }} className="text-indigo-400" />
        <span>Jev đang phân tích và triage thẻ...</span>
      </div>
    )
  }

  if (isDegraded || triageData?.degraded) {
    return (
      <div className="flex items-center gap-2 px-3 py-2 text-xs rounded-lg bg-amber-950/20 border border-amber-500/20 text-amber-300">
        <WarningOutlined style={{ fontSize: 14 }} className="text-amber-400 shrink-0" />
        <span>Gợi ý AI tạm không khả dụng — bạn có thể tiếp tục điền thủ công.</span>
      </div>
    )
  }

  if (!triageData) {
    return null
  }

  const {
    suggestedDomain,
    suggestedWorkType,
    suggestedPriority,
    needsSecurityReview,
    confidenceScore,
    routingAdvice,
    options,
    latencyMs,
  } = triageData

  const confidencePct = formatConfidencePercent(confidenceScore)

  return (
    <div
      data-testid="jev-triage-panel"
      className="p-3.5 rounded-xl bg-slate-900/60 border border-slate-800 backdrop-blur-md transition-all duration-200 space-y-3"
    >
      <div className="flex items-center justify-between gap-2 border-b border-slate-800/80 pb-2">
        <div className="flex items-center gap-2">
          <div className="p-1.5 rounded-md bg-indigo-500/10 text-indigo-400">
            <ThunderboltOutlined style={{ fontSize: 16 }} />
          </div>
          <div>
            <div className="text-xs font-semibold text-slate-200 flex items-center gap-2">
              <span>Phân loại thông minh (Jev System 1)</span>
              <span
                className={`text-[10px] font-medium px-1.5 py-0.5 rounded border ${getAdviceBadgeClass(
                  routingAdvice
                )}`}
              >
                {isDirectAssign(routingAdvice)
                  ? `AI tự tin ${confidencePct}`
                  : isOfferChoices(routingAdvice)
                  ? `AI gợi ý (${confidencePct})`
                  : 'Cần bạn tự chọn'}
              </span>
            </div>
          </div>
        </div>

        {latencyMs > 0 && (
          <div className="flex items-center gap-1 text-[11px] text-slate-400 font-mono">
            <ClockCircleOutlined style={{ fontSize: 12 }} className="text-slate-400" />
            <span>{latencyMs}ms</span>
          </div>
        )}
      </div>

      {needsSecurityReview && (
        <div className="flex items-center gap-2 px-2.5 py-1.5 rounded-md bg-rose-500/10 border border-rose-500/20 text-rose-300 text-xs">
          <SafetyCertificateOutlined style={{ fontSize: 14 }} className="text-rose-400 shrink-0" />
          <span>Thẻ này có yếu tố bảo mật/dữ liệu nhạy cảm cần được rà soát.</span>
        </div>
      )}

      {/* Nhánh 1: DirectAssign (Tự tin cao >= 85%) */}
      {isDirectAssign(routingAdvice) && (
        <div className="space-y-2">
          <div className="grid grid-cols-3 gap-2 text-xs">
            <div className="p-2 rounded-lg bg-slate-800/40 border border-slate-700/50">
              <span className="text-[10px] uppercase tracking-wider text-slate-400 block mb-0.5">
                Miền kỹ thuật
              </span>
              <span className="font-medium text-emerald-400">
                {suggestedDomain || 'Chung'}
              </span>
            </div>
            <div className="p-2 rounded-lg bg-slate-800/40 border border-slate-700/50">
              <span className="text-[10px] uppercase tracking-wider text-slate-400 block mb-0.5">
                Loại hình
              </span>
              <span className="font-medium text-emerald-400">
                {suggestedWorkType || 'Feature'}
              </span>
            </div>
            <div className="p-2 rounded-lg bg-slate-800/40 border border-slate-700/50">
              <span className="text-[10px] uppercase tracking-wider text-slate-400 block mb-0.5">
                Mức ưu tiên
              </span>
              <span className="font-medium text-emerald-400">
                {suggestedPriority || 'Medium'}
              </span>
            </div>
          </div>

          {onApplyDirect && (
            <button
              type="button"
              onClick={() =>
                onApplyDirect({
                  domain: suggestedDomain,
                  workType: suggestedWorkType,
                  priority: suggestedPriority,
                })
              }
              className="w-full flex items-center justify-center gap-1.5 py-1.5 px-3 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-medium shadow-sm transition-colors duration-150"
            >
              <CheckCircleOutlined style={{ fontSize: 14 }} />
              <span>Áp dụng toàn bộ gợi ý</span>
            </button>
          )}
        </div>
      )}

      {/* Nhánh 2: OfferChoices (Tự tin trung bình 50% - 85%) */}
      {isOfferChoices(routingAdvice) && (
        <div className="space-y-2">
          <div className="text-xs text-slate-300">
            Jev đề xuất các lựa chọn tiềm năng nhất. Nhấp để chọn:
          </div>
          <div className="flex flex-wrap gap-1.5">
            {options.map((opt, idx) => {
              const probPct = Math.round(opt.probability * 100)
              return (
                <button
                  key={`${opt.field}-${opt.value}-${idx}`}
                  type="button"
                  onClick={() => onSelectOption?.(opt)}
                  className="flex items-center gap-1.5 px-2.5 py-1 rounded-md text-xs bg-slate-800 hover:bg-slate-700 border border-slate-700 hover:border-indigo-500/50 text-slate-200 transition-all duration-150"
                >
                  <span className="text-[10px] uppercase text-slate-400">
                    {opt.field === 'domain'
                      ? 'Miền'
                      : opt.field === 'workType'
                      ? 'Loại'
                      : 'Ưu tiên'}
                    :
                  </span>
                  <span className="font-medium text-slate-100">{opt.value}</span>
                  {probPct > 0 && (
                    <span className="text-[10px] font-mono text-indigo-400 bg-indigo-950/40 px-1 rounded">
                      {probPct}%
                    </span>
                  )}
                </button>
              )
            })}
          </div>
        </div>
      )}

      {/* Nhánh 3: ManualRequired (Tự tin thấp < 50%) */}
      {isManualRequired(routingAdvice) && (
        <div className="flex items-center gap-2 p-2.5 rounded-lg bg-slate-800/30 border border-slate-700/40 text-xs text-slate-400">
          <QuestionCircleOutlined style={{ fontSize: 16 }} className="text-slate-400 shrink-0" />
          <span>
            Thông tin chưa đủ để Jev tự tin phân loại. Vui lòng tự chọn miền kỹ thuật
            và mức ưu tiên bên dưới.
          </span>
        </div>
      )}
    </div>
  )
}
