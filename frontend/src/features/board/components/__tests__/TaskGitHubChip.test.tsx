import React from 'react'
import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TaskGitHubChip } from '../TaskGitHubChip'

describe('TaskGitHubChip', () => {
  it('returns null when no PR or Issue props are provided', () => {
    const { container } = render(<TaskGitHubChip />)
    expect(container.firstChild).toBeNull()
  })

  it('renders merged PR chip with correct text and purple styling', () => {
    render(
      <TaskGitHubChip
        prNumber={142}
        prUrl="https://github.com/Thinh-TT/TeamNexus/pull/142"
        prStatus="merged"
      />
    )

    const chip = screen.getByTestId('github-pr-chip')
    expect(chip).toBeInTheDocument()
    expect(chip).toHaveTextContent('PR #142 Merged')
    expect(chip).toHaveAttribute('href', 'https://github.com/Thinh-TT/TeamNexus/pull/142')
    expect(chip).toHaveAttribute('target', '_blank')
    expect(chip).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('renders open PR chip with correct text and green styling', () => {
    render(
      <TaskGitHubChip
        prNumber={142}
        prUrl="https://github.com/Thinh-TT/TeamNexus/pull/142"
        prStatus="open"
      />
    )

    const chip = screen.getByTestId('github-pr-chip')
    expect(chip).toBeInTheDocument()
    expect(chip).toHaveTextContent('PR #142 Open')
  })

  it('renders closed PR chip with correct text and gray styling', () => {
    render(
      <TaskGitHubChip
        prNumber={142}
        prUrl="https://github.com/Thinh-TT/TeamNexus/pull/142"
        prStatus="closed"
      />
    )

    const chip = screen.getByTestId('github-pr-chip')
    expect(chip).toBeInTheDocument()
    expect(chip).toHaveTextContent('PR #142 Closed')
  })

  it('renders default PR chip when status is not specified', () => {
    render(
      <TaskGitHubChip
        prNumber={99}
        prUrl="https://github.com/Thinh-TT/TeamNexus/pull/99"
      />
    )

    const chip = screen.getByTestId('github-pr-chip')
    expect(chip).toBeInTheDocument()
    expect(chip).toHaveTextContent('PR #99')
  })

  it('extracts PR number from URL if prNumber prop is not passed', () => {
    render(
      <TaskGitHubChip
        prUrl="https://github.com/owner/repo/pull/77"
        prStatus="open"
      />
    )

    const chip = screen.getByTestId('github-pr-chip')
    expect(chip).toBeInTheDocument()
    expect(chip).toHaveTextContent('PR #77 Open')
  })

  it('renders Issue chip with blue styling and correct number', () => {
    render(
      <TaskGitHubChip
        issueNumber={456}
        issueUrl="https://github.com/owner/repo/issues/456"
      />
    )

    const chip = screen.getByTestId('github-issue-chip')
    expect(chip).toBeInTheDocument()
    expect(chip).toHaveTextContent('Issue #456')
    expect(chip).toHaveAttribute('href', 'https://github.com/owner/repo/issues/456')
    expect(chip).toHaveAttribute('target', '_blank')
    expect(chip).toHaveAttribute('rel', 'noopener noreferrer')
  })

  it('renders both PR and Issue chips when both are provided', () => {
    render(
      <TaskGitHubChip
        prNumber={12}
        prUrl="https://github.com/owner/repo/pull/12"
        prStatus="merged"
        issueNumber={34}
        issueUrl="https://github.com/owner/repo/issues/34"
      />
    )

    expect(screen.getByTestId('github-pr-chip')).toHaveTextContent('PR #12 Merged')
    expect(screen.getByTestId('github-issue-chip')).toHaveTextContent('Issue #34')
  })

  it('stops event propagation on click so parent handlers are not triggered', () => {
    const parentClick = vi.fn()
    render(
      <div onClick={parentClick} data-testid="parent-container">
        <TaskGitHubChip
          prNumber={123}
          prUrl="https://github.com/owner/repo/pull/123"
        />
      </div>
    )

    const chip = screen.getByTestId('github-pr-chip')
    fireEvent.click(chip)

    expect(parentClick).not.toHaveBeenCalled()
  })
})
