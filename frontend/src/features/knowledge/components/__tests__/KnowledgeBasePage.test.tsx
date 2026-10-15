import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { KnowledgeBasePage } from '../KnowledgeBasePage'
import { knowledgeApi } from '../../services/knowledgeApi'
import { useWorkspaceRole } from '../../../../shared/hooks/useWorkspaceRole'
import { useAuthStore } from '../../../auth/store/useAuthStore'
import type { KnowledgeDocument } from '../../types/knowledge.types'

/**
 * Phase 14.7 §B — the knowledge base page.
 *
 * <p>Two claims are load-bearing and are asserted as such: with `enabled: false` the page says the FEATURE is
 * off (rather than that the workspace has no documents), and neither write ever claims the document was
 * written — both queue an action a Manager still has to approve.</p>
 */

vi.mock('../../services/knowledgeApi', () => ({
  knowledgeApi: {
    getDocuments: vi.fn(),
    createDocument: vi.fn(),
    deleteDocument: vi.fn(),
  },
}))

vi.mock('../../../../shared/hooks/useWorkspaceRole', () => ({
  useWorkspaceRole: vi.fn(),
}))

// The header renders the notification bell, which polls the API.
vi.mock('../../../ai/services/notificationApi', () => ({
  notificationApi: {
    listNotifications: vi.fn().mockResolvedValue({ unreadCount: 0, items: [] }),
  },
}))

const mocked = vi.mocked(knowledgeApi)

const documents: KnowledgeDocument[] = [
  {
    id: 'doc-1',
    title: 'Quy trình rà soát bảo mật',
    sourceUrl: 'https://example.com/security',
    status: 'ready',
    chunkCount: 12,
    error: null,
    createdAt: '2026-09-01T08:30:00Z',
    updatedAt: '2026-09-01T08:30:00Z',
  },
  {
    id: 'doc-2',
    title: 'Biên bản họp Sprint 12',
    sourceUrl: null,
    status: 'pending',
    chunkCount: 0,
    error: null,
    createdAt: '2026-09-02T09:00:00Z',
    updatedAt: '2026-09-02T09:00:00Z',
  },
  {
    id: 'doc-3',
    title: 'Tài liệu hỏng',
    sourceUrl: null,
    status: 'failed',
    chunkCount: 0,
    error: 'Không trích xuất được văn bản từ tệp.',
    createdAt: '2026-09-03T10:00:00Z',
    updatedAt: '2026-09-03T10:00:00Z',
  },
]

const actionLog = {
  id: 'log-1',
  action: 'CreateKnowledgeDocument',
  entityType: 'Workspace',
  entityId: 'ws-1',
  status: 'Pending' as const,
  requestedByUserId: 'u-1',
  requestedByName: null,
  decidedByUserId: null,
  decidedByName: null,
  decidedAt: null,
  decisionNote: null,
  taskCount: 0,
  createdAt: '',
  updatedAt: '',
}

const wsId = 'ws-123'

const renderPage = () =>
  render(
    <MemoryRouter initialEntries={[`/workspaces/${wsId}/knowledge`]}>
      <Routes>
        <Route path="/workspaces/:workspaceId/knowledge" element={<KnowledgeBasePage />} />
      </Routes>
    </MemoryRouter>
  )

describe('KnowledgeBasePage (Phase 14.7 §B)', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    useAuthStore.setState({
      user: {
        id: 'u-1',
        email: 'manager@example.com',
        displayName: 'Quản lý',
        avatarUrl: null,
        roles: ['Manager'],
      },
      isAuthenticated: true,
    })

    vi.mocked(useWorkspaceRole).mockReturnValue({
      role: 'Manager',
      isMember: true,
      isManagerOrAdmin: true,
      isAdmin: false,
      isOwner: false,
      loading: false,
    })

    mocked.getDocuments.mockResolvedValue({ enabled: true, items: documents })
    mocked.createDocument.mockResolvedValue(actionLog)
    mocked.deleteDocument.mockResolvedValue(actionLog)
  })

  it('renders one row per document with its status, chunk count and ingest error', async () => {
    renderPage()

    expect(await screen.findByText('Quy trình rà soát bảo mật')).toBeInTheDocument()

    // Statuses are the three the contract defines, in Vietnamese.
    expect(screen.getByText('Đã lập chỉ mục')).toBeInTheDocument()
    expect(screen.getByText('Đang xử lý')).toBeInTheDocument()

    // 'Lỗi' is also a column header, so the STATUS tag is read from its own row.
    const failedRow = screen.getByText('Tài liệu hỏng').closest('tr') as HTMLElement
    expect(within(failedRow).getByText('Lỗi')).toBeInTheDocument()

    expect(screen.getByText('12')).toBeInTheDocument()
    // The failed ingest explains itself rather than leaving an unexplained red tag.
    expect(screen.getByText('Không trích xuất được văn bản từ tệp.')).toBeInTheDocument()

    // The source is a real external link.
    const link = screen.getByRole('link', { name: 'https://example.com/security' })
    expect(link).toHaveAttribute('href', 'https://example.com/security')
    expect(link).toHaveAttribute('target', '_blank')

    // Three rows, three delete controls.
    expect(screen.getByTestId('knowledge-delete-doc-1')).toBeInTheDocument()
    expect(screen.getByTestId('knowledge-delete-doc-3')).toBeInTheDocument()
  })

  it('ENABLED-FALSE_says the feature is off, hides the "no documents" claim and disables the controls', async () => {
    mocked.getDocuments.mockResolvedValue({ enabled: false, items: [] })

    renderPage()

    const offAlert = await screen.findByTestId('knowledge-disabled-alert')

    // The feature is named, so an admin knows which setting to look at.
    expect(offAlert).toHaveTextContent('Jev:RagEnabled')

    // The lie this page must never tell: an empty workspace and a switched-off feature are different facts.
    expect(screen.queryByText(/Chưa có tài liệu nào/)).not.toBeInTheDocument()

    // Nothing can be added or removed while the feature is off.
    expect(screen.getByTestId('knowledge-add-document')).toBeDisabled()
  })

  it('validates locally: an empty title and content never reach the API', async () => {
    renderPage()
    await screen.findByText('Quy trình rà soát bảo mật')

    fireEvent.click(screen.getByTestId('knowledge-add-document'))
    await screen.findByText('Thêm tài liệu vào bộ nhớ ngữ nghĩa')

    fireEvent.click(screen.getByTestId('knowledge-form-submit'))

    expect(await screen.findByText('Vui lòng nhập tiêu đề tài liệu')).toBeInTheDocument()
    expect(screen.getByText('Vui lòng nhập nội dung tài liệu')).toBeInTheDocument()
    // A request that is guaranteed to be refused must never be sent.
    expect(mocked.createDocument).not.toHaveBeenCalled()
  })

  it('CAPS_the form cannot be made to submit more than the server accepts', async () => {
    renderPage()
    await screen.findByText('Quy trình rà soát bảo mật')

    fireEvent.click(screen.getByTestId('knowledge-add-document'))
    await screen.findByText('Thêm tài liệu vào bộ nhớ ngữ nghĩa')

    // The server's own ceilings, enforced on the inputs themselves: typing past them is impossible rather than
    // merely rejected afterwards (and `showCount` on the textarea says how close the content is).
    expect(screen.getByTestId('knowledge-form-title')).toHaveAttribute('maxlength', '300')
    expect(screen.getByTestId('knowledge-form-source-url')).toHaveAttribute('maxlength', '1000')
    expect(screen.getByTestId('knowledge-form-content')).toHaveAttribute('maxlength', '400000')
  })

  it('sends a valid document as a REQUEST and says an approval is pending', async () => {
    renderPage()
    await screen.findByText('Quy trình rà soát bảo mật')

    fireEvent.click(screen.getByTestId('knowledge-add-document'))
    await screen.findByText('Thêm tài liệu vào bộ nhớ ngữ nghĩa')

    fireEvent.change(screen.getByTestId('knowledge-form-title'), {
      target: { value: '  Quy trình mới  ' },
    })
    fireEvent.change(screen.getByTestId('knowledge-form-source-url'), {
      target: { value: 'https://example.com/moi' },
    })
    fireEvent.change(screen.getByTestId('knowledge-form-content'), {
      target: { value: 'Nội dung tài liệu mới' },
    })

    fireEvent.click(screen.getByTestId('knowledge-form-submit'))

    await waitFor(() =>
      expect(mocked.createDocument).toHaveBeenCalledWith(wsId, {
        title: 'Quy trình mới',
        sourceUrl: 'https://example.com/moi',
        content: 'Nội dung tài liệu mới',
      })
    )

    // The wording matters: an action was queued, the document was NOT written.
    expect(
      await screen.findByText(/chờ Manager duyệt/)
    ).toBeInTheDocument()
  })

  it('displays a 400 error message from the server verbatim', async () => {
    const serverMessage = 'Bộ nhớ ngữ nghĩa đang tắt, không thể thêm tài liệu.'
    mocked.createDocument.mockRejectedValue({
      response: { status: 400, data: { error: serverMessage } },
    })

    renderPage()
    await screen.findByText('Quy trình rà soát bảo mật')

    fireEvent.click(screen.getByTestId('knowledge-add-document'))
    await screen.findByText('Thêm tài liệu vào bộ nhớ ngữ nghĩa')

    fireEvent.change(screen.getByTestId('knowledge-form-title'), {
      target: { value: 'Quy trình mới' },
    })
    fireEvent.change(screen.getByTestId('knowledge-form-content'), {
      target: { value: 'Nội dung' },
    })

    fireEvent.click(screen.getByTestId('knowledge-form-submit'))

    // Verbatim — no invented replacement sentence.
    expect(await screen.findByText(serverMessage)).toBeInTheDocument()

    // The modal stays open so the user can fix the input and retry.
    expect(screen.getByTestId('knowledge-form-submit')).toBeInTheDocument()
  })

  it('DELETES only after the confirmation is accepted', async () => {
    renderPage()
    await screen.findByText('Quy trình rà soát bảo mật')

    fireEvent.click(screen.getByTestId('knowledge-delete-doc-1'))

    // Asking is not doing: the popconfirm appears first.
    expect(mocked.deleteDocument).not.toHaveBeenCalled()

    const confirm = await screen.findByRole('button', { name: 'Gửi yêu cầu' })
    fireEvent.click(confirm)

    await waitFor(() => expect(mocked.deleteDocument).toHaveBeenCalledWith(wsId, 'doc-1'))
    // The row is still there: the document is only removed once a Manager approves.
    expect(screen.getByText('Quy trình rà soát bảo mật')).toBeInTheDocument()
    expect(screen.getByText(/chờ Manager duyệt/)).toBeInTheDocument()
  })

  it('hides the write controls from a plain Member', async () => {
    vi.mocked(useWorkspaceRole).mockReturnValue({
      role: 'Member',
      isMember: true,
      isManagerOrAdmin: false,
      isAdmin: false,
      isOwner: false,
      loading: false,
    })

    renderPage()
    await screen.findByText('Quy trình rà soát bảo mật')

    // Reading is Member+; writing is Manager+, and the API enforces that — the UI says so up front.
    expect(screen.getByTestId('knowledge-add-document')).toBeDisabled()
    expect(screen.getByTestId('knowledge-delete-doc-1')).toBeDisabled()
  })

  it('emits no Tailwind utility class anywhere in the page', async () => {
    const { container } = renderPage()
    await screen.findByText('Quy trình rà soát bảo mật')

    fireEvent.click(screen.getByTestId('knowledge-add-document'))
    await screen.findByText('Thêm tài liệu vào bộ nhớ ngữ nghĩa')

    // TeamNexus has no Tailwind: these class names would render as nothing, so their presence is the bug.
    const tailwindish =
      /(^|\s)(bg-|text-\[|border-|space-[xy]-|grid-cols-|gap-\d|p-\d|px-\d|py-\d|rounded-|flex-wrap|backdrop-|animate-|shrink-0)/

    const offenders: string[] = []
    container.querySelectorAll<HTMLElement>('*').forEach((el) => {
      const className = typeof el.className === 'string' ? el.className : ''
      if (className && tailwindish.test(className)) {
        offenders.push(`${el.tagName.toLowerCase()}: ${className}`)
      }
    })

    expect(
      offenders,
      `Found Tailwind classes in KnowledgeBasePage — the project uses pure Ant Design:\n${offenders.join('\n')}`
    ).toEqual([])
  })

  it('renders the alert body as its own element so the error cannot glue to the title', async () => {
    mocked.getDocuments.mockRejectedValue({
      response: { status: 500, data: { error: 'Máy chủ đang bận.' } },
    })

    renderPage()

    const alert = await screen.findByTestId('knowledge-error-alert')
    expect(within(alert).getByText('Máy chủ đang bận.')).toBeInTheDocument()

    // A list that FAILED to load is not an empty list: claiming "no documents" would be the same lie the
    // feature-off state avoids.
    expect(screen.queryByText(/Chưa có tài liệu nào/)).not.toBeInTheDocument()
  })
})
