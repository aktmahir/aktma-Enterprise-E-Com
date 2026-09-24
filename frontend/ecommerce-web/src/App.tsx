import { useEffect, useState } from 'react'
import './App.css'

type Product = { id: string; name: string; description: string; category: string; price: number; currency: string }
type ProductResponse = { items: Product[]; totalCount: number }
type CartItem = Product & { quantity: number }
const apiUrl = import.meta.env.VITE_GATEWAY_URL ?? 'http://localhost:5101'
const customerStorageKey = 'north-form-customer-id'
const tokenStorageKey = 'north-form-access-token'

function App() {
  const [products, setProducts] = useState<Product[]>([])
  const [cart, setCart] = useState<CartItem[]>(() => { try { return JSON.parse(localStorage.getItem('north-form-cart') ?? '[]') as CartItem[] } catch { return [] } })
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState('All pieces')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [bagOpen, setBagOpen] = useState(false)
  const [checkoutOpen, setCheckoutOpen] = useState(false)
  const [checkoutEmail, setCheckoutEmail] = useState('')
  const [checkoutState, setCheckoutState] = useState('')
  const [authOpen, setAuthOpen] = useState(false)
  const [authEmail, setAuthEmail] = useState('')
  const [authPassword, setAuthPassword] = useState('')
  const [authState, setAuthState] = useState('')
  const [accessToken, setAccessToken] = useState(() => localStorage.getItem(tokenStorageKey) ?? '')
  const [customerId] = useState(() => { const existing = localStorage.getItem(customerStorageKey); if (existing) return existing; const id = crypto.randomUUID(); localStorage.setItem(customerStorageKey, id); return id })

  useEffect(() => {
    const controller = new AbortController(); const params = new URLSearchParams({ pageSize: '24' })
    if (search.trim()) params.set('search', search.trim()); if (category !== 'All pieces') params.set('category', category)
    fetch(`${apiUrl}/api/v1/catalog/products?${params}`, { signal: controller.signal })
      .then((response) => { if (!response.ok) throw new Error('Catalog unavailable'); return response.json() as Promise<ProductResponse> })
      .then((data) => { setProducts(data.items); setError('') }).catch((reason: Error) => { if (reason.name !== 'AbortError') setError('We could not reach the catalog. Try again shortly.') }).finally(() => setLoading(false))
    return () => controller.abort()
  }, [category, search])

  const addToBag = (product: Product) => { setCart((items) => { const updated = items.find((item) => item.id === product.id) ? items.map((item) => item.id === product.id ? { ...item, quantity: item.quantity + 1 } : item) : [...items, { ...product, quantity: 1 }]; localStorage.setItem('north-form-cart', JSON.stringify(updated)); return updated }); setBagOpen(true) }
  const removeFromBag = (id: string) => setCart((items) => { const updated = items.filter((item) => item.id !== id); localStorage.setItem('north-form-cart', JSON.stringify(updated)); return updated })
  const total = cart.reduce((sum, item) => sum + item.price * item.quantity, 0)
  const categories = ['All pieces', 'Apparel', 'Home', 'Stationery', 'Accessories']
  const changeCategory = (value: string) => { setLoading(true); setCategory(value) }
  const changeSearch = (value: string) => { setLoading(true); setSearch(value) }

  return <main>
    <nav className="nav-shell"><a className="wordmark" href="/">north / form</a><div className="nav-links"><a href="#catalog">Shop</a><a href="#story">Journal</a><button className="icon-button" onClick={() => { if (accessToken) { localStorage.removeItem(tokenStorageKey); setAccessToken('') } else setAuthOpen(true) }}>{accessToken ? 'Logout' : 'Login'}</button><button className="icon-button" onClick={() => setBagOpen(true)} aria-label="Open shopping bag">Bag <span>{cart.reduce((sum, item) => sum + item.quantity, 0)}</span></button></div></nav>
    <section className="hero" id="story"><div className="hero-copy"><p className="eyebrow">Edition 01 / considered essentials</p><h1>Objects with a quieter point of view.</h1><p className="hero-text">North / Form brings useful things into focus: tactile, enduring pieces for the spaces and rituals that make up a life.</p><a className="text-link" href="#catalog">Explore the edit <span>↘</span></a></div><div className="hero-art" aria-label="Abstract composition of a ceramic vessel and linen fabric" role="img"><div className="sun"></div><div className="vessel"></div><div className="cloth"></div><div className="art-label">material study<br />01 — 04</div></div></section>
    <section className="catalog-section" id="catalog"><div className="section-heading"><div><p className="eyebrow">The current edit</p><h2>Made to be kept.</h2></div><p className="result-count">{loading ? 'Finding pieces...' : `${products.length} pieces selected`}</p></div><div className="catalog-toolbar"><div className="categories" aria-label="Filter by category">{categories.map((item) => <button key={item} className={category === item ? 'active' : ''} onClick={() => changeCategory(item)}>{item}</button>)}</div><label className="search"><span>⌕</span><input value={search} onChange={(event) => changeSearch(event.target.value)} placeholder="Search the edit" /></label></div>{error && <div className="state error">{error}</div>}{!error && loading && <div className="state">Loading the current edit...</div>}{!error && !loading && products.length === 0 && <div className="state">No pieces match that search.</div>}{!error && !loading && products.length > 0 && <div className="product-grid">{products.map((product, index) => <article className={`product-card tone-${index % 4}`} key={product.id}><div className="product-image"><span>{String(index + 1).padStart(2, '0')}</span><div className="product-shape"></div></div><div className="product-meta"><div><p className="product-category">{product.category}</p><h3>{product.name}</h3></div><p className="price">{new Intl.NumberFormat('en-US', { style: 'currency', currency: product.currency }).format(product.price)}</p></div><p className="description">{product.description}</p><button className="add-button" onClick={() => addToBag(product)}>Add to bag <span>+</span></button></article>)}</div>}</section>
    <footer><span>north / form</span><span>Useful things, thoughtfully made.</span><span>© 2026</span></footer>
    {bagOpen && <aside className="side-panel"><button className="close-button" onClick={() => setBagOpen(false)} aria-label="Close bag">×</button><p className="eyebrow">Your selection</p><h2>Bag</h2>{cart.length === 0 ? <p className="panel-note">Your bag is waiting for something useful.</p> : <>{cart.map((item) => <div className="bag-item" key={item.id}><div><strong>{item.name}</strong><small>{item.quantity} × ${item.price.toFixed(2)}</small></div><button onClick={() => removeFromBag(item.id)} aria-label={`Remove ${item.name}`}>Remove</button></div>)}<div className="bag-total"><span>Total</span><strong>${total.toFixed(2)}</strong></div><button className="checkout-button" onClick={() => setCheckoutOpen(true)}>Checkout</button></>}</aside>}
    {checkoutOpen && <div className="modal-backdrop"><form className="checkout-modal" onSubmit={async (event) => { event.preventDefault(); setCheckoutState('Submitting order...'); try { const response = await fetch(`${apiUrl}/api/v1/orders`, { method: 'POST', headers: { 'Content-Type': 'application/json', ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}) }, body: JSON.stringify({ customerId, currency: 'USD', items: cart.map((item) => ({ productId: item.id, name: item.name, quantity: item.quantity, unitPrice: item.price })) }) }); if (!response.ok) throw new Error('Order submission failed'); const order = await response.json() as { id: string }; setCheckoutState('Order received. Confirming inventory and payment...'); for (let attempt = 0; attempt < 12; attempt += 1) { await new Promise((resolve) => setTimeout(resolve, 500)); const statusResponse = await fetch(`${apiUrl}/api/v1/orders/${order.id}`); if (!statusResponse.ok) continue; const current = await statusResponse.json() as { status: string }; if (current.status === 'Confirmed') { setCheckoutState(`Order confirmed for ${checkoutEmail}.`); setCart([]); localStorage.removeItem('north-form-cart'); return } if (current.status === 'Failed') { setCheckoutState('Order could not be confirmed because inventory or payment failed.'); return } } setCheckoutState('Order is still processing. Check back shortly for its final status.') } catch { setCheckoutState('Order could not be submitted. Please try again.') } }}><button type="button" className="close-button" onClick={() => setCheckoutOpen(false)} aria-label="Close checkout">×</button><p className="eyebrow">Secure checkout</p><h2>Almost yours.</h2>{checkoutState ? <p className="panel-note success">{checkoutState}</p> : <><label>Email<input required type="email" value={checkoutEmail} onChange={(event) => setCheckoutEmail(event.target.value)} placeholder="you@example.com" /></label><label>Payment token<input required value="dev-token" onChange={() => undefined} /></label><button className="checkout-button" type="submit">Place order · ${total.toFixed(2)}</button></>}</form></div>}
    {authOpen && <div className="modal-backdrop"><form className="checkout-modal" onSubmit={async (event) => { event.preventDefault(); setAuthState('Signing in...'); try { const response = await fetch(`${apiUrl}/api/v1/auth/login`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ email: authEmail, password: authPassword }) }); if (!response.ok) throw new Error('Invalid credentials'); const result = await response.json() as { accessToken: string }; localStorage.setItem(tokenStorageKey, result.accessToken); setAccessToken(result.accessToken); setAuthState('Signed in.'); setAuthOpen(false) } catch { setAuthState('Invalid credentials.') } }}><button type="button" className="close-button" onClick={() => setAuthOpen(false)} aria-label="Close login">×</button><p className="eyebrow">Customer account</p><h2>Welcome back.</h2><label>Email<input required type="email" value={authEmail} onChange={(event) => setAuthEmail(event.target.value)} /></label><label>Password<input required type="password" value={authPassword} onChange={(event) => setAuthPassword(event.target.value)} /></label><button className="checkout-button" type="submit">Sign in</button>{authState && <p className="panel-note">{authState}</p>}</form></div>}
  </main>
}

export default App