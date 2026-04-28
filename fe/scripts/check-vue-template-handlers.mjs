import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs'
import path from 'node:path'
import { parse } from '@vue/compiler-sfc'

const root = process.cwd()
const inputPaths = process.argv.slice(2)
const targets = inputPaths.length > 0 ? inputPaths : ['src']
const failures = []

const isVueFile = (file) => file.endsWith('.vue')

const collectVueFiles = (target, files = []) => {
  const resolved = path.resolve(root, target)

  if (!existsSync(resolved)) {
    return files
  }

  const stats = statSync(resolved)

  if (stats.isDirectory()) {
    for (const child of readdirSync(resolved)) {
      if (child === 'node_modules' || child === 'dist' || child === 'coverage') {
        continue
      }

      collectVueFiles(path.join(resolved, child), files)
    }

    return files
  }

  if (stats.isFile() && isVueFile(resolved)) {
    files.push(resolved)
  }

  return files
}

const walk = (node, visit) => {
  if (!node || typeof node !== 'object') {
    return
  }

  visit(node)

  for (const key of ['children', 'branches']) {
    const children = node[key]

    if (Array.isArray(children)) {
      for (const child of children) {
        walk(child, visit)
      }
    }
  }
}

const isExplicitFunction = (expression) =>
  /^(?:async\s*)?(?:\([^)]*\)|[$_A-Za-z][$_A-Za-z0-9]*)\s*=>/.test(expression) ||
  /^(?:async\s+)?function\b/.test(expression)

const isSingleJavaScriptExpression = (expression) => {
  try {
    new Function(`return (${expression})`)
    return true
  } catch {
    return false
  }
}

const shouldAllowHandler = (expression) => {
  const trimmed = expression.trim()

  if (!trimmed.includes('\n')) {
    return true
  }

  if (isExplicitFunction(trimmed)) {
    return true
  }

  return isSingleJavaScriptExpression(trimmed)
}

const describeEvent = (prop) => {
  const arg = prop.arg?.content
  return arg ? `@${arg}` : 'v-on'
}

for (const file of targets.flatMap((target) => collectVueFiles(target))) {
  const source = readFileSync(file, 'utf8')
  const result = parse(source, { filename: file })

  if (result.errors.length > 0) {
    continue
  }

  const ast = result.descriptor.template?.ast

  walk(ast, (node) => {
    if (!Array.isArray(node.props)) {
      return
    }

    for (const prop of node.props) {
      if (prop.type !== 7 || prop.name !== 'on' || !prop.exp?.content) {
        continue
      }

      if (shouldAllowHandler(prop.exp.content)) {
        continue
      }

      failures.push({
        file,
        line: prop.loc.start.line,
        column: prop.loc.start.column,
        event: describeEvent(prop),
      })
    }
  })
}

if (failures.length > 0) {
  console.error('Found multi-line Vue event handlers that are not single expressions.')
  console.error('Wrap multi-statement handlers in an arrow function or move them into named methods.')

  for (const failure of failures) {
    const relative = path.relative(root, failure.file)
    console.error(`- ${relative}:${failure.line}:${failure.column} ${failure.event}`)
  }

  process.exit(1)
}
