export interface RawSseEvent {
  event: string
  data: string
}

export interface ParseSseResult {
  events: RawSseEvent[]
  carry: string
}

/**
 * Parses raw SSE chunk buffer into events and remaining unparsed carry.
 * SSE frames are delimited by a double newline: \n\n or \r\n\r\n.
 */
export function parseSseFrames(buffer: string): ParseSseResult {
  if (!buffer) {
    return { events: [], carry: '' }
  }

  // Normalize Windows line breaks \r\n to \n for uniform boundary detection
  // To preserve carry accurately without altering characters, find boundary positions in original buffer
  const events: RawSseEvent[] = []
  let searchIndex = 0

  while (searchIndex < buffer.length) {
    // Look for double newlines: \r\n\r\n or \n\n
    const nextRNRN = buffer.indexOf('\r\n\r\n', searchIndex)
    const nextNN = buffer.indexOf('\n\n', searchIndex)

    let frameEndIndex = -1
    let delimiterLength = 0

    if (nextRNRN !== -1 && nextNN !== -1) {
      if (nextRNRN <= nextNN) {
        frameEndIndex = nextRNRN
        delimiterLength = 4
      } else {
        frameEndIndex = nextNN
        delimiterLength = 2
      }
    } else if (nextRNRN !== -1) {
      frameEndIndex = nextRNRN
      delimiterLength = 4
    } else if (nextNN !== -1) {
      frameEndIndex = nextNN
      delimiterLength = 2
    } else {
      // No more complete frames in this buffer
      break
    }

    const rawBlock = buffer.slice(searchIndex, frameEndIndex)
    searchIndex = frameEndIndex + delimiterLength

    if (!rawBlock.trim()) {
      continue
    }

    const lines = rawBlock.split(/\r?\n/)
    let eventName = 'message'
    const dataLines: string[] = []
    let hasData = false

    for (const line of lines) {
      if (!line || line.startsWith(':')) {
        // Comment or empty line
        continue
      }

      if (line.startsWith('event:')) {
        let name = line.slice(6)
        if (name.startsWith(' ')) {
          name = name.slice(1)
        }
        eventName = name.trim()
      } else if (line.startsWith('data:')) {
        hasData = true
        let content = line.slice(5)
        if (content.startsWith(' ')) {
          content = content.slice(1)
        }
        dataLines.push(content)
      }
    }

    if (hasData) {
      events.push({
        event: eventName,
        data: dataLines.join('\n'),
      })
    }
  }

  const carry = buffer.slice(searchIndex)
  return { events, carry }
}
