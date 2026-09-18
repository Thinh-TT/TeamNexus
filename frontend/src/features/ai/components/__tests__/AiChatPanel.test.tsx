import React from 'react'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { AiChatPanel } from '../AiChatPanel'
import * as useAiChatModule from '../../hooks/useAiChat'

vi.mock('../../hooks/useAiChat')

describe('AiChatPanel', () => {
  const defaultHookMock: useAiChatModule.UseAiChatReturn = {
    messages: [],
    streamingAnswer: '',
    meta: null,
    status: 'idle',
    error: null,
    httpStatus: null,
    ask: vi.fn(),
    cancel: vi.fn(),
    saveAnswer: vi.fn(),
    clearError: vi.fn(),
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(useAiChatModule.useAiChat).mockReturnValue({ ...defaultHookMock })
  })

  it('renders initial friendly prompt suggestions when conversation is empty', () => {
    render(<AiChatPanel taskId="task-1" />)
    expect(screen.getByText('Hỏi AI về nhiệm vụ này')).toBeInTheDocument()
    expect(screen.getByText('💡 Tóm tắt tiến độ')).toBeInTheDocument()
    expect(screen.getByText('🚀 Gợi ý các bước tiếp theo')).toBeInTheDocument()
  })

  it('fills input when clicking on a suggestion tag and submits with Enter', () => {
    const askMock = vi.fn()
    vi.mocked(useAiChatModule.useAiChat).mockReturnValue({
      ...defaultHookMock,
      ask: askMock,
    })

    render(<AiChatPanel taskId="task-1" />)
    const suggestionTag = screen.getByText('💡 Tóm tắt tiến độ')
    fireEvent.click(suggestionTag)

    const textarea = screen.getByPlaceholderText(/Hỏi AI về nhiệm vụ này/i) as HTMLTextAreaElement
    expect(textarea.value).toContain('Tóm tắt tiến độ')

    fireEvent.keyDown(textarea, { key: 'Enter', shiftKey: false })
    expect(askMock).toHaveBeenCalledWith('Tóm tắt tiến độ và các điểm cần lưu ý của nhiệm vụ này')
  })

  it('allows newline without sending when Shift+Enter is pressed', () => {
    const askMock = vi.fn()
    vi.mocked(useAiChatModule.useAiChat).mockReturnValue({
      ...defaultHookMock,
      ask: askMock,
    })

    render(<AiChatPanel taskId="task-1" />)
    const textarea = screen.getByPlaceholderText(/Hỏi AI về nhiệm vụ này/i)
    fireEvent.change(textarea, { target: { value: 'Dòng 1' } })
    fireEvent.keyDown(textarea, { key: 'Enter', shiftKey: true })

    expect(askMock).not.toHaveBeenCalled()
  })

  it('renders live streaming answer with Stop button when streaming', () => {
    const cancelMock = vi.fn()
    vi.mocked(useAiChatModule.useAiChat).mockReturnValue({
      ...defaultHookMock,
      status: 'streaming',
      streamingAnswer: 'AI đang soạn phản hồi...',
      cancel: cancelMock,
    })

    render(<AiChatPanel taskId="task-1" />)
    expect(screen.getByText('AI đang soạn phản hồi...')).toBeInTheDocument()

    const stopButton = screen.getByRole('button', { name: /Dừng/i })
    expect(stopButton).toBeInTheDocument()

    fireEvent.click(stopButton)
    expect(cancelMock).toHaveBeenCalledTimes(1)
  })

  it('renders messages and handles "Lưu thành bình luận"', async () => {
    const saveAnswerMock = vi.fn().mockResolvedValue({})
    vi.mocked(useAiChatModule.useAiChat).mockReturnValue({
      ...defaultHookMock,
      messages: [
        { id: '1', role: 'user', content: 'Câu hỏi của tôi', createdAt: '' },
        { id: '2', role: 'assistant', content: 'Câu trả lời của AI', createdAt: '' },
      ],
      saveAnswer: saveAnswerMock,
    })

    render(<AiChatPanel taskId="task-1" />)
    expect(screen.getByText('Câu hỏi của tôi')).toBeInTheDocument()
    expect(screen.getByText('Câu trả lời của AI')).toBeInTheDocument()

    const saveBtn = screen.getByTestId('ai-chat-save-btn')
    expect(saveBtn).not.toBeDisabled()

    fireEvent.click(saveBtn)
    await waitFor(() => {
      expect(saveAnswerMock).toHaveBeenCalledWith('Câu trả lời của AI')
    })
  })

  it('disables save button when answer exceeds 2000 characters', () => {
    const veryLongAnswer = 'X'.repeat(2005)
    vi.mocked(useAiChatModule.useAiChat).mockReturnValue({
      ...defaultHookMock,
      messages: [
        { id: '1', role: 'user', content: 'Hỏi', createdAt: '' },
        { id: '2', role: 'assistant', content: veryLongAnswer, createdAt: '' },
      ],
    })

    render(<AiChatPanel taskId="task-1" />)
    const saveBtn = screen.getByTestId('ai-chat-save-btn')
    expect(saveBtn).toBeDisabled()
  })

  it('displays Vietnamese error alert when hook has error state', () => {
    const clearErrorMock = vi.fn()
    vi.mocked(useAiChatModule.useAiChat).mockReturnValue({
      ...defaultHookMock,
      status: 'error',
      error: 'Không thể kết nối tới dịch vụ AI',
      clearError: clearErrorMock,
    })

    render(<AiChatPanel taskId="task-1" />)
    expect(screen.getByText('Không thể kết nối tới dịch vụ AI')).toBeInTheDocument()
  })
})
