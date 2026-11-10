import React from 'react'
import { GithubOutlined } from '@ant-design/icons'
import { Space, Tag, Tooltip } from 'antd'
import { parseGitHubUrl } from '../utils/githubUrlParser'

export interface TaskGitHubChipProps {
  prNumber?: number | null
  prUrl?: string | null
  prStatus?: string | null // 'open' | 'merged' | 'closed'
  issueNumber?: number | null
  issueUrl?: string | null
  className?: string
  style?: React.CSSProperties
}

export const TaskGitHubChip: React.FC<TaskGitHubChipProps> = ({
  prNumber,
  prUrl,
  prStatus,
  issueNumber,
  issueUrl,
  className,
  style,
}) => {
  const parsedPr = prUrl ? parseGitHubUrl(prUrl) : null
  const effectivePrNumber = prNumber ?? (parsedPr?.type === 'pr' ? parsedPr.number : undefined)
  const hasPr = Boolean(effectivePrNumber || prUrl)

  const parsedIssue = issueUrl ? parseGitHubUrl(issueUrl) : null
  const effectiveIssueNumber =
    issueNumber ?? (parsedIssue?.type === 'issue' ? parsedIssue.number : undefined)
  const hasIssue = Boolean(effectiveIssueNumber || issueUrl)

  if (!hasPr && !hasIssue) {
    return null
  }

  // --- Render PR Chip ---
  let prElement: React.ReactNode = null
  if (hasPr) {
    const status = prStatus?.toLowerCase()

    let labelText = effectivePrNumber ? `PR #${effectivePrNumber}` : 'PR'
    let color = '#475569'
    let borderColor = '#cbd5e1'
    let backgroundColor = '#f8fafc'

    if (status === 'merged') {
      labelText = effectivePrNumber ? `PR #${effectivePrNumber} Merged` : 'PR Merged'
      color = '#8b5cf6'
      borderColor = '#7c3aed'
      backgroundColor = '#f5f3ff'
    } else if (status === 'open') {
      labelText = effectivePrNumber ? `PR #${effectivePrNumber} Open` : 'PR Open'
      color = '#10b981'
      borderColor = '#10b981'
      backgroundColor = '#ecfdf5'
    } else if (status === 'closed') {
      labelText = effectivePrNumber ? `PR #${effectivePrNumber} Closed` : 'PR Closed'
      color = '#64748b'
      borderColor = '#64748b'
      backgroundColor = '#f1f5f9'
    }

    const prTag = (
      <Tag
        icon={<GithubOutlined style={{ color }} />}
        style={{
          margin: 0,
          display: 'inline-flex',
          alignItems: 'center',
          gap: 4,
          fontSize: 10.5,
          fontWeight: 600,
          lineHeight: '18px',
          padding: '0 6px',
          borderRadius: 5,
          color,
          borderColor,
          backgroundColor,
          cursor: prUrl ? 'pointer' : 'default',
          transition: 'all 0.15s ease',
        }}
      >
        {labelText}
      </Tag>
    )

    if (prUrl) {
      prElement = (
        <Tooltip title={`GitHub PR: ${prUrl}`} key="github-pr-chip-tooltip">
          <a
            href={prUrl}
            target="_blank"
            rel="noopener noreferrer"
            onClick={(e) => e.stopPropagation()}
            onPointerDown={(e) => e.stopPropagation()}
            style={{ textDecoration: 'none', display: 'inline-flex' }}
            data-testid="github-pr-chip"
            aria-label={`GitHub ${labelText}`}
          >
            {prTag}
          </a>
        </Tooltip>
      )
    } else {
      prElement = (
        <span
          key="github-pr-chip-span"
          data-testid="github-pr-chip"
          style={{ display: 'inline-flex' }}
          onClick={(e) => e.stopPropagation()}
          onPointerDown={(e) => e.stopPropagation()}
        >
          {prTag}
        </span>
      )
    }
  }

  // --- Render Issue Chip ---
  let issueElement: React.ReactNode = null
  if (hasIssue) {
    const labelText = effectiveIssueNumber ? `Issue #${effectiveIssueNumber}` : 'Issue'
    const color = '#3b82f6'
    const borderColor = '#3b82f6'
    const backgroundColor = '#eff6ff'

    const issueTag = (
      <Tag
        icon={<GithubOutlined style={{ color }} />}
        style={{
          margin: 0,
          display: 'inline-flex',
          alignItems: 'center',
          gap: 4,
          fontSize: 10.5,
          fontWeight: 600,
          lineHeight: '18px',
          padding: '0 6px',
          borderRadius: 5,
          color,
          borderColor,
          backgroundColor,
          cursor: issueUrl ? 'pointer' : 'default',
          transition: 'all 0.15s ease',
        }}
      >
        {labelText}
      </Tag>
    )

    if (issueUrl) {
      issueElement = (
        <Tooltip title={`GitHub Issue: ${issueUrl}`} key="github-issue-chip-tooltip">
          <a
            href={issueUrl}
            target="_blank"
            rel="noopener noreferrer"
            onClick={(e) => e.stopPropagation()}
            onPointerDown={(e) => e.stopPropagation()}
            style={{ textDecoration: 'none', display: 'inline-flex' }}
            data-testid="github-issue-chip"
            aria-label={`GitHub ${labelText}`}
          >
            {issueTag}
          </a>
        </Tooltip>
      )
    } else {
      issueElement = (
        <span
          key="github-issue-chip-span"
          data-testid="github-issue-chip"
          style={{ display: 'inline-flex' }}
          onClick={(e) => e.stopPropagation()}
          onPointerDown={(e) => e.stopPropagation()}
        >
          {issueTag}
        </span>
      )
    }
  }

  if (hasPr && hasIssue) {
    return (
      <Space size={4} align="center" className={className} style={style}>
        {prElement}
        {issueElement}
      </Space>
    )
  }

  return (
    <span className={className} style={{ display: 'inline-flex', ...style }}>
      {prElement || issueElement}
    </span>
  )
}
