import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useBoardStore } from '../../stores/boardStore'
import {
  KEEP_ALIVE_INTERVAL_MS,
  SERVER_TIMEOUT_MS,
  useBoardHub,
} from '../useBoardHub'

const { mockBuilder, mockConnection, mockHandlers, callbacks } = vi.hoisted(() => {
  const handlers: Record<string, Function> = {}
  const cbs = {
    reconnecting: null as Function | null,
    reconnected: null as Function | null,
    close: null as Function | null,
  }

  const connection = {
    state: 'Connected',
    serverTimeoutInMilliseconds: 0,
    keepAliveIntervalInMilliseconds: 0,
    start: vi.fn().mockResolvedValue(undefined),
    stop: vi.fn().mockResolvedValue(undefined),
    invoke: vi.fn().mockResolvedValue(undefined),
    on: vi.fn((event: string, handler: Function) => {
      handlers[event] = handler
    }),
    onreconnecting: vi.fn((cb: Function) => {
      cbs.reconnecting = cb
    }),
    onreconnected: vi.fn((cb: Function) => {
      cbs.reconnected = cb
    }),
    onclose: vi.fn((cb: Function) => {
      cbs.close = cb
    }),
  }

  const builder = {
    withUrl: vi.fn().mockReturnThis(),
    withAutomaticReconnect: vi.fn().mockReturnThis(),
    configureLogging: vi.fn().mockReturnThis(),
    build: vi.fn().mockReturnValue(connection),
  }

  return {
    mockBuilder: builder,
    mockConnection: connection,
    mockHandlers: handlers,
    callbacks: cbs,
  }
})

vi.mock('@microsoft/signalr', () => {
  return {
    HubConnectionBuilder: vi.fn(function () {
      return mockBuilder
    }),
    HubConnectionState: {
      Connected: 'Connected',
      Disconnected: 'Disconnected',
      Connecting: 'Connecting',
      Reconnecting: 'Reconnecting',
    },
    LogLevel: {
      Warning: 3,
    },
  }
})

describe('useBoardHub', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockConnection.state = 'Connected'
    useBoardStore.setState({
      board: null,
      columns: [],
      tasksByColumn: {},
      connectionStatus: 'disconnected',
    })
  })

  afterEach(() => {
    vi.clearAllMocks()
    vi.unstubAllEnvs()
  })

  it('configures connection with /hubs/board and credentials by default (relative dev proxy)', async () => {
    renderHook(() => useBoardHub('board-1'))

    expect(mockBuilder.withUrl).toHaveBeenCalledWith('/hubs/board', {
      withCredentials: true,
    })
    expect(mockConnection.start).toHaveBeenCalled()
    expect(mockConnection.serverTimeoutInMilliseconds).toBe(SERVER_TIMEOUT_MS)
    expect(mockConnection.keepAliveIntervalInMilliseconds).toBe(KEEP_ALIVE_INTERVAL_MS)

    await vi.waitFor(() => {
      expect(mockConnection.invoke).toHaveBeenCalledWith('JoinBoard', 'board-1')
      expect(useBoardStore.getState().connectionStatus).toBe('connected')
    })
  })

  it('configures connection with resolved hub URL when VITE_API_BASE_URL is set', () => {
    vi.stubEnv('VITE_API_BASE_URL', 'https://api.example.com/api')

    renderHook(() => useBoardHub('board-1'))

    expect(mockBuilder.withUrl).toHaveBeenCalledWith('https://api.example.com/hubs/board', {
      withCredentials: true,
    })
  })

  it('configures withAutomaticReconnect using retry policy object rather than static array', () => {
    renderHook(() => useBoardHub('board-1'))

    expect(mockBuilder.withAutomaticReconnect).toHaveBeenCalledWith(
      expect.objectContaining({
        nextRetryDelayInMilliseconds: expect.any(Function),
      })
    )

    const passedPolicy = mockBuilder.withAutomaticReconnect.mock.calls[0][0]
    expect(passedPolicy.nextRetryDelayInMilliseconds({ previousRetryCount: 0 })).toBe(0)
    expect(passedPolicy.nextRetryDelayInMilliseconds({ previousRetryCount: 1 })).toBe(2000)
    expect(passedPolicy.nextRetryDelayInMilliseconds({ previousRetryCount: 4 })).toBe(30000)
    expect(passedPolicy.nextRetryDelayInMilliseconds({ previousRetryCount: 10 })).toBe(30000)
  })

  it('registers all 11 real-time event handlers including AgentRunProgress', () => {
    renderHook(() => useBoardHub('board-1'))

    expect(mockConnection.on).toHaveBeenCalledWith('TaskCreated', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('TaskUpdated', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('TaskMoved', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('TaskDeleted', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('ColumnCreated', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('ColumnUpdated', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('ColumnsReordered', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('ColumnDeleted', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('CommentAdded', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('CommentDeleted', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('AgentRunProgress', expect.any(Function))
  })

  it('dispatches TaskCreated event only when boardId matches', () => {
    renderHook(() => useBoardHub('board-1'))

    // Event for another board (should be ignored)
    act(() => {
      mockHandlers['TaskCreated']({ id: 't-other', boardId: 'board-2', columnId: 'col-1' })
    })
    expect(useBoardStore.getState().tasksByColumn['col-1']).toBeUndefined()

    // Event for this board
    act(() => {
      mockHandlers['TaskCreated']({
        id: 't-1',
        boardId: 'board-1',
        columnId: 'col-1',
        title: 'Real-time Task',
        position: 0,
        labels: [],
        commentCount: 0,
      })
    })

    const tasks = useBoardStore.getState().tasksByColumn['col-1']
    expect(tasks).toHaveLength(1)
    expect(tasks[0].title).toBe('Real-time Task')
  })

  it('handles reconnecting, reconnected (with refetch spy), and close lifecycles', async () => {
    const refetchSpy = vi.fn().mockResolvedValue(undefined)
    renderHook(() => useBoardHub('board-1', refetchSpy))

    await vi.waitFor(() => {
      expect(useBoardStore.getState().connectionStatus).toBe('connected')
    })

    // Simulate network loss -> reconnecting
    act(() => {
      callbacks.reconnecting?.()
    })
    expect(useBoardStore.getState().connectionStatus).toBe('reconnecting')

    // Simulate network restored -> reconnected
    await act(async () => {
      await callbacks.reconnected?.()
    })
    expect(useBoardStore.getState().connectionStatus).toBe('connected')
    expect(mockConnection.invoke).toHaveBeenCalledWith('JoinBoard', 'board-1')
    expect(refetchSpy).toHaveBeenCalledTimes(1)

    // Simulate connection closed
    act(() => {
      callbacks.close?.()
    })
    expect(useBoardStore.getState().connectionStatus).toBe('disconnected')
  })

  it('provides manual reconnect() function that restarts connection when disconnected', async () => {
    const refetchSpy = vi.fn().mockResolvedValue(undefined)
    const { result } = renderHook(() => useBoardHub('board-1', refetchSpy))

    await vi.waitFor(() => {
      expect(useBoardStore.getState().connectionStatus).toBe('connected')
    })

    // Simulate disconnection
    mockConnection.state = 'Disconnected'
    act(() => {
      callbacks.close?.()
    })
    expect(useBoardStore.getState().connectionStatus).toBe('disconnected')

    // Invoke manual reconnect
    mockConnection.start.mockClear()
    mockConnection.invoke.mockClear()

    await act(async () => {
      await result.current.reconnect()
    })

    expect(mockConnection.start).toHaveBeenCalledTimes(1)
    expect(mockConnection.invoke).toHaveBeenCalledWith('JoinBoard', 'board-1')
    expect(refetchSpy).toHaveBeenCalledTimes(1)
    expect(useBoardStore.getState().connectionStatus).toBe('connected')
  })

  it('invokes LeaveBoard and stops connection when unmounting', async () => {
    const { unmount } = renderHook(() => useBoardHub('board-1'))

    await vi.waitFor(() => {
      expect(mockConnection.invoke).toHaveBeenCalledWith('JoinBoard', 'board-1')
    })

    unmount()

    expect(mockConnection.invoke).toHaveBeenCalledWith('LeaveBoard', 'board-1')
  })
})
