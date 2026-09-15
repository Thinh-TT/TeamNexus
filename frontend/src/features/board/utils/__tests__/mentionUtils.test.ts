import { describe, expect, it } from 'vitest'
import { extractMentionUserIds } from '../mentionUtils'
import type { WorkspaceMemberResponse } from '../../types/board.types'

describe('mentionUtils - extractMentionUserIds', () => {
  const members: WorkspaceMemberResponse[] = [
    {
      userId: 'u-1',
      displayName: 'Trần An',
      email: 'an@example.com',
      role: 'Member',
      joinedAt: '',
      memberType: 'human',
    },
    {
      userId: 'u-2',
      displayName: 'Trần An Bình',
      email: 'binh@example.com',
      role: 'Member',
      joinedAt: '',
      memberType: 'human',
    },
    {
      userId: 'u-3',
      displayName: 'Duy',
      email: 'duy@example.com',
      role: 'Member',
      joinedAt: '',
      memberType: 'human',
    },
    {
      userId: 'u-dup-1',
      displayName: 'Thảo Nhi',
      email: 'thao1@example.com',
      role: 'Member',
      joinedAt: '',
      memberType: 'human',
    },
    {
      userId: 'u-dup-2',
      displayName: 'Thảo Nhi',
      email: 'thao2@example.com',
      role: 'Member',
      joinedAt: '',
      memberType: 'human',
    },
    {
      userId: 'agent-1',
      displayName: 'Nexus Assistant',
      email: 'agent@example.com',
      role: 'Member',
      joinedAt: '',
      memberType: 'ai_agent',
    },
  ]

  it('matches single-word display name', () => {
    const result = extractMentionUserIds('Chào @Duy nhé', members)
    expect(result).toEqual(['u-3'])
  })

  it('matches multi-word display name with spaces', () => {
    const result = extractMentionUserIds('Nhờ @Trần An kiểm tra giúp', members)
    expect(result).toEqual(['u-1'])
  })

  it('resolves longer nested names before shorter prefix names', () => {
    const result = extractMentionUserIds('Nhờ @Trần An Bình xem lại', members)
    expect(result).toEqual(['u-2'])
  })

  it('matches both people when both nested and prefix names are mentioned', () => {
    const result = extractMentionUserIds('Nhờ @Trần An Bình và @Trần An xem lại', members)
    expect(result).toContain('u-1')
    expect(result).toContain('u-2')
    expect(result).toHaveLength(2)
  })

  it('does NOT count @ within an email address as a mention', () => {
    const result = extractMentionUserIds('Liên hệ qua an@example.com hoặc test@Duy.vn nhé', members)
    expect(result).toEqual([])
  })

  it('deduplicates when the same person is mentioned multiple times', () => {
    const result = extractMentionUserIds('@Duy ơi, nhắc lại @Duy kiểm tra nhé', members)
    expect(result).toEqual(['u-3'])
  })

  it('filters out current user (self-mention)', () => {
    const result = extractMentionUserIds('Tôi là @Duy đây', members, 'u-3')
    expect(result).toEqual([])
  })

  it('returns empty array when content contains no mentions', () => {
    const result = extractMentionUserIds('Nội dung bình thường không có tag', members)
    expect(result).toEqual([])
  })

  it('picks the first userId when two members have identical displayNames', () => {
    const result = extractMentionUserIds('Gửi @Thảo Nhi nhé', members)
    expect(result).toEqual(['u-dup-1'])
  })

  it('ignores AI Agent members in mentions', () => {
    const result = extractMentionUserIds('Nhờ @Nexus Assistant làm việc', members)
    expect(result).toEqual([])
  })
})
